namespace FocusTracker.Domain.Models;

public class AppSettings
{
    public const int DefaultIdleThresholdMinutes = 3;
    public const int MinIdleThresholdMinutes = 1;
    public const int MaxIdleThresholdMinutes = 60;

    // Скільки хвилин без введення, звуку чи повноекранного вікна до того, як час перестає рахуватись
    public int IdleThresholdMinutes { get; set; } = DefaultIdleThresholdMinutes;

    public AppSettings Normalized() => new()
    {
        IdleThresholdMinutes = Math.Clamp(IdleThresholdMinutes, MinIdleThresholdMinutes, MaxIdleThresholdMinutes)
    };
}
