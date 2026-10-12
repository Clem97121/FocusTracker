namespace FocusTracker.Domain.Models;

public class AppSettings
{
    public const int DefaultIdleThresholdMinutes = 3;
    public const int MinIdleThresholdMinutes = 1;
    public const int MaxIdleThresholdMinutes = 60;

    // Скільки хвилин без введення, звуку чи повноекранного вікна до того, як час перестає рахуватись
    public int IdleThresholdMinutes { get; set; } = DefaultIdleThresholdMinutes;

    // Ідентифікатор цього пристрою в хмарній базі; створюється один раз
    public Guid DeviceId { get; set; }

    // Назва пристрою, яку бачать інші пристрої (за замовчуванням — ім'я комп'ютера)
    public string DeviceName { get; set; } = string.Empty;

    public bool CloudSyncEnabled { get; set; }

    // Рядок підключення до хмарної бази, зашифрований DPAPI для поточного користувача Windows
    public string? CloudConnectionProtected { get; set; }

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    public AppSettings Normalized()
    {
        var copy = Clone();
        copy.IdleThresholdMinutes = Math.Clamp(IdleThresholdMinutes, MinIdleThresholdMinutes, MaxIdleThresholdMinutes);
        if (copy.DeviceId == Guid.Empty)
            copy.DeviceId = Guid.NewGuid();
        if (string.IsNullOrWhiteSpace(copy.DeviceName))
            copy.DeviceName = Environment.MachineName;
        copy.DeviceName = copy.DeviceName.Trim();
        return copy;
    }
}
