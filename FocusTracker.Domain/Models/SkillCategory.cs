using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations.Schema;

namespace FocusTracker.Domain.Models;

public class SkillCategory : ObservableObject
{
    public int Id { get; set; }

    private string _name = string.Empty;
    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    private string _color = string.Empty;
    public string Color
    {
        get => _color;
        set => Set(ref _color, value);
    }

    private int _sortOrder;
    public int SortOrder
    {
        get => _sortOrder;
        set => Set(ref _sortOrder, value);
    }

    // UI-состояния
    private bool _isEditing;
    [NotMapped]
    public bool IsEditing
    {
        get => _isEditing;
        set => Set(ref _isEditing, value);
    }

    private bool _isAddingSkill;
    [NotMapped]
    public bool IsAddingSkill
    {
        get => _isAddingSkill;
        set => Set(ref _isAddingSkill, value);
    }

    private bool _isExpanded;
    [NotMapped]
    public bool IsExpanded
    {
        get => _isExpanded;
        set => Set(ref _isExpanded, value);
    }

    private string _newSkillName = "";
    [NotMapped]
    public string NewSkillName
    {
        get => _newSkillName;
        set => Set(ref _newSkillName, value);
    }

    private ObservableCollection<Skill> _skills = new();
    [NotMapped]
    public ObservableCollection<Skill> Skills
    {
        get => _skills;
        set => Set(ref _skills, value);
    }
}
