using Xunit;
using FocusTracker.Data;
using FocusTracker.Data.Services;
using FocusTracker.Domain;
using FocusTracker.Domain.Models;

namespace FocusTracker.Tests;

public class IdleDetectorTests
{
    private static readonly DateTime Start = new(2026, 10, 12, 12, 0, 0);

    [Fact]
    public void Present_within_threshold_after_last_signal()
    {
        var idle = new IdleDetector(Start, TimeSpan.FromMinutes(3));

        Assert.True(idle.IsPresent(Start.AddMinutes(3)));
        Assert.False(idle.IsPresent(Start.AddMinutes(3).AddSeconds(1)));
    }

    [Fact]
    public void Signal_extends_presence_and_ignores_older_signals()
    {
        var idle = new IdleDetector(Start, TimeSpan.FromMinutes(3));

        idle.Signal(Start.AddMinutes(10));
        idle.Signal(Start.AddMinutes(5));

        Assert.Equal(Start.AddMinutes(10), idle.LastSignal);
        Assert.True(idle.IsPresent(Start.AddMinutes(12)));
        Assert.False(idle.IsPresent(Start.AddMinutes(14)));
    }

    [Fact]
    public void Ignore_idle_counts_time_without_signals()
    {
        var idle = new IdleDetector(Start, TimeSpan.FromMinutes(3));

        Assert.True(idle.IsPresent(Start.AddHours(2), ignoresIdle: true));
    }

    [Fact]
    public void Threshold_can_be_changed_at_runtime()
    {
        var idle = new IdleDetector(Start, TimeSpan.FromMinutes(3));

        idle.Threshold = TimeSpan.FromMinutes(10);
        Assert.True(idle.IsPresent(Start.AddMinutes(8)));
    }
}

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"focustracker-settings-{Guid.NewGuid():N}");
    private string SettingsPath => Path.Combine(_dir, "settings.json");

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var store = new SettingsStore(SettingsPath);

        Assert.Equal(AppSettings.DefaultIdleThresholdMinutes, store.Current.IdleThresholdMinutes);
    }

    [Fact]
    public void Saved_settings_are_loaded_and_reported()
    {
        var store = new SettingsStore(SettingsPath);
        AppSettings? changed = null;
        store.Changed += s => changed = s;

        store.Save(new AppSettings { IdleThresholdMinutes = 7 });

        Assert.Equal(7, changed?.IdleThresholdMinutes);
        Assert.Equal(7, new SettingsStore(SettingsPath).Current.IdleThresholdMinutes);
    }

    [Fact]
    public void Out_of_range_values_are_clamped()
    {
        var store = new SettingsStore(SettingsPath);

        store.Save(new AppSettings { IdleThresholdMinutes = 0 });
        Assert.Equal(AppSettings.MinIdleThresholdMinutes, store.Current.IdleThresholdMinutes);

        store.Save(new AppSettings { IdleThresholdMinutes = 1000 });
        Assert.Equal(AppSettings.MaxIdleThresholdMinutes, store.Current.IdleThresholdMinutes);
    }

    [Fact]
    public void Corrupted_file_gives_defaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, "{ not json");

        Assert.Equal(AppSettings.DefaultIdleThresholdMinutes, new SettingsStore(SettingsPath).Current.IdleThresholdMinutes);
    }

    [Fact]
    public void Removed_settings_in_old_file_are_ignored()
    {
        // Колишні поля локального режиму Коуча (ключ Anthropic і «Мозок у хмарі»)
        Directory.CreateDirectory(_dir);
        var deviceId = Guid.NewGuid();
        File.WriteAllText(SettingsPath, $$"""
            {
              "IdleThresholdMinutes": 9,
              "DeviceId": "{{deviceId}}",
              "CloudSyncEnabled": true,
              "JarvisEnabled": true,
              "AnthropicKeyProtected": "AQAAANCMnd8BFdERjHoAwE",
              "Goals": "1. Робота",
              "AssistantInCloud": true
            }
            """);

        var current = new SettingsStore(SettingsPath).Current;

        Assert.Equal(9, current.IdleThresholdMinutes);
        Assert.Equal(deviceId, current.DeviceId);
        Assert.True(current.CloudSyncEnabled);
        Assert.True(current.JarvisEnabled);
        Assert.Equal("1. Робота", current.Goals);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }
}

public class IgnoreIdleProgramsTests
{
    [Fact]
    public async Task Returns_only_programs_marked_ignore_idle()
    {
        using var db = new TestDb();
        var tracked = new TrackedProgramService(db);
        tracked.Add(new TrackedProgram { Name = "vlc", Identifier = "vlc", Category = "Суміжні", IgnoreIdle = true });
        tracked.Add(new TrackedProgram { Name = "code", Identifier = "code", Category = "Продуктивні" });

        var identifiers = await new ProgramService(db, tracked).GetIgnoreIdleIdentifiersAsync();

        Assert.Equal(new[] { "vlc" }, identifiers);
        Assert.Contains("VLC", identifiers);
    }
}
