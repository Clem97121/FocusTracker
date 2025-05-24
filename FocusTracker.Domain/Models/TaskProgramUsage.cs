namespace FocusTracker.Domain.Models
{
    public class TaskProgramUsage
    {
        public int Id { get; set; }
        public int TaskId { get; set; }
        public int ProgramId { get; set; }

        // Храним секунды, но не переименовываем поле
        public int CountedActiveMinutes { get; set; }
        public int InitialActiveSeconds { get; set; } // новое поле — активное время на момент создания задачи

        public TaskItem Task { get; set; }
        public TrackedProgram Program { get; set; }
        public string? RecordedAt { get; set; }
    }
}
