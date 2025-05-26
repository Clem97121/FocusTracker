namespace FocusTracker.Domain.Models;

public class TaskProgram
{
    public int TaskId { get; set; }
    public int ProgramId { get; set; }

    public TaskItem Task { get; set; } = null!;
    public TrackedProgram Program { get; set; } = null!;
}
