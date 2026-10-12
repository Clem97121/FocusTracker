using FocusTracker.Domain.Models;
using System.ComponentModel.DataAnnotations.Schema;

public class TaskItem : ISyncEntity
{
    public int Id { get; set; }
    public Guid SyncId { get; set; }
    public DateTime? SyncUpdatedAt { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public int SkillId { get; set; }
    public int EstimatedMinutes { get; set; }
    public int Difficulty { get; set; }
    public DateTime DateCreated { get; set; }
    public bool Completed { get; set; }
    [NotMapped]
    public List<TrackedProgram> Programs { get; set; } = new();
    public Skill Skill { get; set; }

    [NotMapped]
    public bool IsEditing { get; set; }
    [NotMapped]
    public DateTime? LastRemindedAt { get; set; }
    public int? EarnedXp { get; set; }      // 🔹 nullable, бо є незавершені задачі
    public int? ActiveSeconds { get; set; } // 🔹 теж nullable
    public bool IsPassive { get; set; } = false;

}
