using FocusTracker.Domain.Interfaces;
using FocusTracker.Domain.Models;

namespace FocusTracker.Data.Services
{
    public class TaskProgramUsageService : ITaskProgramUsageService
    {
        private readonly FocusTrackerDbContext _db;

        public TaskProgramUsageService(FocusTrackerDbContext db)
        {
            _db = db;
        }

        public List<TaskProgramUsage> GetByTaskId(int taskId)
        {
            return _db.TaskProgramUsages
                .Where(u => u.TaskId == taskId)
                .ToList();
        }


        public void AddOrUpdate(TaskProgramUsage usage)
        {
            var existing = _db.TaskProgramUsages
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
                _db.TaskProgramUsages.Add(usage);
            }

            _db.SaveChanges();
        }




        public List<TaskProgramUsage> GetAll()
        {
            return _db.TaskProgramUsages.ToList();
        }


    }
}
