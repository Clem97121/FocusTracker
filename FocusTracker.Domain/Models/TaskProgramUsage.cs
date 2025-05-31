namespace FocusTracker.Domain.Models
{
    public class TaskProgramUsage
    {
        public int Id { get; set; }
        public int TaskId { get; set; }
        public int ProgramId { get; set; }

        public int CountedActiveSeconds { get; set; }
        public int InitialActiveSeconds { get; set; }

        public int InitialPassiveSeconds { get; set; }
        public int CountedPassiveSeconds { get; set; } // 🆕 нове поле

        public DateTime? RecordedAt { get; set; }
        public bool IsFinalized { get; set; }

        public TaskItem Task { get; set; } = null!;
        public TrackedProgram Program { get; set; } = null!;
    }
}
