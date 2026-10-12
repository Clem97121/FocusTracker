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

        // День, до якого відносяться Initial*Seconds (денна статистика на момент прив'язки).
        // null у старих записах: тоді береться дата створення задачі.
        public DateTime? BaselineDate { get; set; }

        public DateTime? RecordedAt { get; set; }
        public bool IsFinalized { get; set; }

        public TaskItem Task { get; set; } = null!;
        public TrackedProgram Program { get; set; } = null!;
    }
}
