using Xunit;
using FocusTracker.Data;
using FocusTracker.Data.Cloud;
using FocusTracker.Data.Services;
using FocusTracker.Domain;
using FocusTracker.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace FocusTracker.Tests;

public class CloudSyncTests
{
    private static readonly Guid Laptop = Guid.NewGuid();
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    private static CloudHourlyRow Row(string app, int hour, double totalSeconds, DateOnly? date = null) =>
        new(Laptop, app, date ?? Today, hour, totalSeconds, totalSeconds / 2, DateTime.UtcNow);

    [Fact]
    public async Task Remote_rows_are_inserted_then_overwritten_and_programs_added()
    {
        using var db = new TestDb();
        var sync = new CloudSyncService(db);

        await sync.ApplyRemoteRowsAsync(new[] { Row("chrome", 10, 600), Row("code", 10, 300) });
        await sync.ApplyRemoteRowsAsync(new[] { Row("chrome", 10, 900) });

        using var ctx = db.CreateDbContext();
        var rows = await ctx.RemoteHourlyUsages.OrderBy(r => r.AppName).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(TimeSpan.FromSeconds(900), rows[0].TotalTime);
        Assert.Equal(TimeSpan.FromSeconds(450), rows[0].ActiveTime);
        Assert.Equal(new[] { "chrome", "code" }, await ctx.TrackedPrograms.Select(p => p.Identifier).OrderBy(n => n).ToListAsync());
    }

    [Fact]
    public async Task Combined_stats_follow_selected_device()
    {
        using var db = new TestDb();
        var local = new AppUsageStatService(db);
        await local.AddOrUpdateUsageAsync("chrome", TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
        await new CloudSyncService(db).ApplyRemoteRowsAsync(new[] { Row("chrome", 10, 120), Row("code", 11, 30) });

        var scope = new StatsScope();
        var combined = new CombinedAppUsageStatService(local, db, scope);

        var all = await combined.GetStatsForTodayAsync();
        Assert.Equal(TimeSpan.FromSeconds(180), all.Single(s => s.AppName == "chrome").TotalTime);
        Assert.Equal(TimeSpan.FromSeconds(30), all.Single(s => s.AppName == "code").TotalTime);

        scope.SetThisDevice();
        Assert.Equal(TimeSpan.FromSeconds(60), Assert.Single(await combined.GetStatsForTodayAsync()).TotalTime);

        scope.SetRemoteDevice(Laptop);
        var laptop = await combined.GetStatsForTodayAsync();
        Assert.Equal(TimeSpan.FromSeconds(120), laptop.Single(s => s.AppName == "chrome").TotalTime);
        Assert.Equal(2, laptop.Count);

        scope.SetAllDevices();
        var hourly = await combined.GetHourlyStatsAsync(DateTime.Today);
        Assert.Contains(hourly, h => h.AppName == "code" && h.Hour == 11);
    }

    [Fact]
    public async Task Daily_limit_counts_time_from_all_devices()
    {
        using var db = new TestDb();
        var program = new TrackedProgram { Name = "game", Identifier = "game", Category = "Непродуктивні" };
        new TrackedProgramService(db).Add(program);
        await new RestrictionService(db).CreateAsync(new Restriction
        {
            Note = "limit",
            Rules = new List<RestrictionRule> { new() { RuleType = "max_total_seconds", Value = "1" } },
            Targets = new List<RestrictionToProgram> { new() { ProgramId = program.Id } }
        });

        var evaluator = new RestrictionEvaluator(db);
        await new AppUsageStatService(db).AddOrUpdateUsageAsync("game", TimeSpan.FromSeconds(40), TimeSpan.Zero);
        Assert.False(evaluator.TryGetViolatedRestrictions("game", TimeSpan.Zero, out _));

        // 40 с тут + 40 с на ноутбуці > 1 хв
        await new CloudSyncService(db).ApplyRemoteRowsAsync(new[] { Row("game", 10, 40) });
        Assert.True(evaluator.TryGetViolatedRestrictions("game", TimeSpan.Zero, out _));
    }

    [Fact]
    public async Task Local_usage_marks_rows_as_changed()
    {
        using var db = new TestDb();
        var before = DateTime.Now;

        await new AppUsageStatService(db).AddOrUpdateUsageAsync("code", TimeSpan.FromSeconds(5), TimeSpan.Zero);

        using var ctx = db.CreateDbContext();
        Assert.True((await ctx.HourlyAppUsageLogs.SingleAsync()).UpdatedAt >= before);
    }
}

public sealed class DeviceSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"focustracker-device-{Guid.NewGuid():N}");
    private string SettingsPath => Path.Combine(_dir, "settings.json");

    [Fact]
    public void Device_id_is_created_once_and_kept()
    {
        var first = new SettingsStore(SettingsPath).Current;

        Assert.NotEqual(Guid.Empty, first.DeviceId);
        Assert.Equal(Environment.MachineName, first.DeviceName);
        Assert.Equal(first.DeviceId, new SettingsStore(SettingsPath).Current.DeviceId);
    }

    [Fact]
    public void Update_keeps_other_fields()
    {
        var store = new SettingsStore(SettingsPath);
        store.Update(s => s.CloudSyncEnabled = true);
        store.Update(s => s.IdleThresholdMinutes = 9);

        var reloaded = new SettingsStore(SettingsPath).Current;
        Assert.True(reloaded.CloudSyncEnabled);
        Assert.Equal(9, reloaded.IdleThresholdMinutes);
        Assert.Equal(store.Current.DeviceId, reloaded.DeviceId);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }
}
