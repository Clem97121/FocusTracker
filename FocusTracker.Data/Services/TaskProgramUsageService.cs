using FocusTracker.Domain.Interfaces;
using FocusTracker.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace FocusTracker.Data.Services
{
    public class TaskProgramUsageService : ITaskProgramUsageService
    {
        private readonly IDbContextFactory<FocusTrackerDbContext> _dbFactory;

        public TaskProgramUsageService(IDbContextFactory<FocusTrackerDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public List<TaskProgramUsage> GetByTaskId(int taskId)
        {
            using var db = _dbFactory.CreateDbContext();
            return db.TaskProgramUsages
                .AsNoTracking()
                .Where(u => u.TaskId == taskId)
                .ToList();
        }

        public void AddOrUpdate(TaskProgramUsage usage)
        {
            using var db = _dbFactory.CreateDbContext();
            var existing = db.TaskProgramUsages
                .FirstOrDefault(u => u.TaskId == usage.TaskId && u.ProgramId == usage.ProgramId);

            if (existing != null)
            {
                existing.CountedActiveSeconds = usage.CountedActiveSeconds;
                existing.CountedPassiveSeconds = usage.CountedPassiveSeconds;
                existing.InitialActiveSeconds = usage.InitialActiveSeconds;
                existing.InitialPassiveSeconds = usage.InitialPassiveSeconds;

                if (usage.RecordedAt.HasValue)
                    existing.RecordedAt = usage.RecordedAt;

                existing.IsFinalized = usage.IsFinalized;
            }
            else
            {
                db.TaskProgramUsages.Add(usage);
            }

            db.SaveChanges();
        }

        public List<TaskProgramUsage> GetAll()
        {
            using var db = _dbFactory.CreateDbContext();
            return db.TaskProgramUsages.AsNoTracking().ToList();
        }
    }
}
