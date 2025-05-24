namespace FocusTracker.Domain.Models
{
    public class ExperienceHistory
    {
        public int Id { get; set; }
        public int TaskId { get; set; }
        public int XpEarned { get; set; }
        public string RecordedAt { get; set; }

        public TaskItem Task { get; set; }
    }
}
