using FocusTracker.Domain;
namespace FocusTracker.Domain.Models;

public class TrackedProgram : ObservableObject
{
    public int Id { get; set; }

    private string _name = string.Empty;
    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    private string _identifier = string.Empty;
    public string Identifier
    {
        get => _identifier;
        set => Set(ref _identifier, value);
    }

    private string _category = string.Empty;
    public string Category
    {
        get => _category;
        set => Set(ref _category, value);
    }

    private bool _isTracked;
    public bool IsTracked
    {
        get => _isTracked;
        set => Set(ref _isTracked, value);
    }

    private bool _isHidden;
    public bool IsHidden
    {
        get => _isHidden;
        set => Set(ref _isHidden, value);
    }

    private string? _displayName;
    public string? DisplayName
    {
        get => _displayName;
        set => Set(ref _displayName, value);
    }

    private byte[]? _iconBytes;
    public byte[]? IconBytes
    {
        get => _iconBytes;
        set => Set(ref _iconBytes, value);
    }

    private DateTime? _lastUsed;
    public DateTime? LastUsed
    {
        get => _lastUsed;
        set => Set(ref _lastUsed, value);
    }

    private TimeSpan? _totalUsageTime;
    public TimeSpan? TotalUsageTime
    {
        get => _totalUsageTime;
        set => Set(ref _totalUsageTime, value);
    }
}
