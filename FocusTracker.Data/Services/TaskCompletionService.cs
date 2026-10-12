using FocusTracker.Domain.Interfaces;
using FocusTracker.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace FocusTracker.Data.Services
{
    public class TaskCompletionService : ITaskCompletionService
    {
        private readonly IDbContextFactory<FocusTrackerDbContext> _dbFactory;
        private readonly ISkillService _skillService;

        public TaskCompletionService(IDbContextFactory<FocusTrackerDbContext> dbFactory, ISkillService skillService)
        {
            _dbFactory = dbFactory;
            _skillService = skillService;
        }

        public int Complete(int taskId)
        {
            var now = DateTime.Now;
            int totalSeconds = 0;
            TaskItem? task;

            using (var db = _dbFactory.CreateDbContext())
            {
                task = db.TaskItems.Find(taskId);
                if (task == null || task.Completed)
                    return 0;

                var programs = db.TaskPrograms
                    .Where(tp => tp.TaskId == taskId)
                    .Select(tp => tp.Program)
                    .ToList();

                foreach (var program in programs)
                {
                    var usage = db.TaskProgramUsages
                        .FirstOrDefault(u => u.TaskId == taskId && u.ProgramId == program.Id);

                    var baselineDate = (usage?.BaselineDate ?? task.DateCreated).Date;
                    int initial = task.IsPassive
                        ? usage?.InitialPassiveSeconds ?? 0
                        : usage?.InitialActiveSeconds ?? 0;

                    // Увесь час у програмі з дня прив'язки до сьогодні, а не лише за сьогодні,
                    // щоб задача, що тягнеться через північ, теж рахувалась
                    var dailyStats = db.AppUsageStats
                        .Where(s => s.AppName == program.Identifier && s.Date >= baselineDate)
                        .AsNoTracking()
                        .ToList();
                    int sinceBaseline = (int)dailyStats.Sum(s => (task.IsPassive ? s.TotalTime : s.ActiveTime).TotalSeconds) - initial;

                    // Час, уже зарахований іншими задачами після створення цієї, не рахуємо вдруге
                    int usedByOthers = db.TaskProgramUsages
                        .Where(u => u.ProgramId == program.Id
                                 && u.TaskId != taskId
                                 && u.IsFinalized
                                 && u.RecordedAt.HasValue
                                 && u.RecordedAt > task.DateCreated)
                        .AsNoTracking()
                        .ToList()
                        .Sum(u => task.IsPassive ? u.CountedPassiveSeconds : u.CountedActiveSeconds);

                    int newSeconds = Math.Max(0, sinceBaseline - usedByOthers);
                    totalSeconds += newSeconds;

                    if (usage == null)
                    {
                        usage = new TaskProgramUsage
                        {
                            TaskId = taskId,
                            ProgramId = program.Id,
                            BaselineDate = baselineDate
                        };
                        db.TaskProgramUsages.Add(usage);
                    }

                    usage.CountedActiveSeconds = task.IsPassive ? 0 : newSeconds;
                    usage.CountedPassiveSeconds = task.IsPassive ? newSeconds : 0;
                    usage.RecordedAt = now;
                    usage.IsFinalized = true;
                }

                int xp = ITaskCompletionService.CalculateXp(totalSeconds, task.Difficulty);

                // Задача завершується завжди, навіть без нового часу (тоді 0 XP),
                // інакше вона застрягала б у списку назавжди
                task.Completed = true;
                task.EarnedXp = xp;
                task.ActiveSeconds = totalSeconds;

                if (xp > 0)
                {
                    db.ExperienceHistories.Add(new ExperienceHistory
                    {
                        TaskId = taskId,
                        XpEarned = xp,
                        RecordedAt = now.ToString("s")
                    });
                }

                db.SaveChanges();

                if (xp > 0)
                    _skillService.AddXp(task.SkillId, xp);

                return xp;
            }
        }
    }
}
