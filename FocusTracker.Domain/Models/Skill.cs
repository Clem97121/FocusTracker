using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using FocusTracker.Domain;

namespace FocusTracker.Domain.Models;

public class Skill : ObservableObject, ISyncEntity
{
    public int Id { get; set; }
    public Guid SyncId { get; set; }
    public DateTime? SyncUpdatedAt { get; set; }

    private string _name = string.Empty;
    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    private int _categoryId;
    public int CategoryId
    {
        get => _categoryId;
        set => Set(ref _categoryId, value);
    }

    private int _level;
    public int Level
    {
        get => _level;
        set => Set(ref _level, value);
    }

    private int _xp;
    public int Xp
    {
        get => _xp;
        set => Set(ref _xp, value);
    }

    [NotMapped]
    public int XpToNextLevel => XpForLevel(Level);

    // XP, потрібне, щоб перейти з рівня level на наступний
    public static int XpForLevel(int level) => 100 * Math.Max(level, 1);

    [NotMapped]
    public double XpProgressPercent => XpToNextLevel == 0 ? 0 : (double)Xp / XpToNextLevel * 100;

    public SkillCategory Category { get; set; } = null!;
    public ICollection<TaskItem> Tasks { get; set; } = new List<TaskItem>();

    private bool _isEditing;
    [NotMapped]
    public bool IsEditing
    {
        get => _isEditing;
        set => Set(ref _isEditing, value);
    }
    [NotMapped]
    public string XpDisplay => $"{Xp} / {XpToNextLevel} XP";

}
