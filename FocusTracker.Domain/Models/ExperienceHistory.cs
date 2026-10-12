namespace FocusTracker.Domain.Models
{
    public class ExperienceHistory : ISyncEntity
    {
        public int Id { get; set; }
        public Guid SyncId { get; set; }
        public DateTime? SyncUpdatedAt { get; set; }
        public int TaskId { get; set; }
        public int XpEarned { get; set; }
        public string RecordedAt { get; set; } = string.Empty;

        public TaskItem? Task { get; set; } 
    }
}
