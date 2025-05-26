using System.ComponentModel.DataAnnotations.Schema;

namespace FocusTracker.Domain.Models;

public class RestrictionRule : ObservableObject
{
    public int Id { get; set; }

    private int _restrictionId;
    public int RestrictionId
    {
        get => _restrictionId;
        set => Set(ref _restrictionId, value);
    }

    private string _ruleType = string.Empty;
    public string RuleType
    {
        get => _ruleType;
        set => Set(ref _ruleType, value);
    }

    private string _value = string.Empty;
    public string Value
    {
        get => _value;
        set => Set(ref _value, value);
    }

    public Restriction Restriction { get; set; } = null!;

    // 🖼 UI-поля
    private string _displayName = string.Empty;
    [NotMapped]
    public string DisplayName
    {
        get => _displayName;
        set => Set(ref _displayName, value);
    }
}
