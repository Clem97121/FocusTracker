using FocusTracker.Domain.Models;

namespace FocusTracker.Domain.Interfaces
{
    public interface ITaskProgramUsageService
    {
        List<TaskProgramUsage> GetByTaskId(int taskId);
        void AddOrUpdate(TaskProgramUsage usage);
        // 👇 добавь вот эту строку:
        List<TaskProgramUsage> GetAll();
    }
}
