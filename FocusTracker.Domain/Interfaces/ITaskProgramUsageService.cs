using FocusTracker.Domain.Models;

namespace FocusTracker.Domain.Interfaces
{
    public interface ITaskProgramUsageService
    {
        List<TaskProgramUsage> GetByTaskId(int taskId);
        void AddOrUpdate(TaskProgramUsage usage);
        List<TaskProgramUsage> GetAll();
        void Delete(int taskId, int programId);
    }
}
