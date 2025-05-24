using FocusTracker.Domain.Models;
using System.ComponentModel.DataAnnotations.Schema;

public class TaskItem
{
    public int Id { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public int SkillId { get; set; }
    public int EstimatedMinutes { get; set; }
    public int Difficulty { get; set; }

    public string CreatedAt { get; set; }
    public DateTime DateCreated { get; set; } // 🔹 используем для фильтрации по дате
    public bool Completed { get; set; }

    public List<TrackedProgram> Programs { get; set; } = new();
    public Skill Skill { get; set; }

    [NotMapped]
    public bool IsEditing { get; set; }
    [NotMapped]
    public DateTime? LastRemindedAt { get; set; }
    public int? EarnedXp { get; set; }      // 🔹 nullable, бо є незавершені задачі
    public int? ActiveSeconds { get; set; } // 🔹 теж nullable

}
