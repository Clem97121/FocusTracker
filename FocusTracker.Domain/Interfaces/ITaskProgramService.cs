using FocusTracker.Domain.Models;

public interface ITaskProgramService
{
    List<TaskProgram> GetAll();

    List<TaskProgram> GetByTaskId(int taskId);
    void Add(TaskProgram taskProgram);
    void DeleteByTaskId(int taskId);

    Task<List<TrackedProgram>> GetProgramsForTaskAsync(int taskId); // ← добавить
}
