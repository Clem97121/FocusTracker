using FocusTracker.Domain;
namespace FocusTracker.Domain.Models;

public class TrackedProgram : ObservableObject
{
    public int Id { get; set; }

    private string _name = string.Empty;
    public string Name
    {
        get => _name;
        set { if (Set(ref _name, value)) RaisePropertyChanged(nameof(Title)); }
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

    // Час рахується навіть без введення (відео без звуку, дзвінки тощо)
    private bool _ignoreIdle;
    public bool IgnoreIdle
    {
        get => _ignoreIdle;
        set => Set(ref _ignoreIdle, value);
    }

    private string? _displayName;
    public string? DisplayName
    {
        get => _displayName;
        set { if (Set(ref _displayName, value)) RaisePropertyChanged(nameof(Title)); }
    }

    // Назва для інтерфейсу: опис з exe, а якщо його немає (програма з телефона чи іншого ПК) — ім'я процесу/програми
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string Title => string.IsNullOrWhiteSpace(DisplayName) ? Name : DisplayName;

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

    // Коли користувач востаннє змінював налаштування програми (UTC). Для синхронізації між
    // пристроями: перемагає новіша зміна. null — налаштування ще не змінювались.
    public DateTime? SettingsUpdatedAt { get; set; }

    private TimeSpan? _totalUsageTime;
    public TimeSpan? TotalUsageTime
    {
        get => _totalUsageTime;
        set => Set(ref _totalUsageTime, value);
    }
}
