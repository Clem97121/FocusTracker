using Xunit;
using FocusTracker.Data.Cloud;
using FocusTracker.Data.Services;
using FocusTracker.Domain;
using FocusTracker.Domain.Models;

namespace FocusTracker.Tests;

public class ComputerTimeTests
{
    private static readonly DateTime Day = new(2026, 10, 12);

    private static IEnumerable<DateTime> Minutes(int fromHour, int count) =>
        Enumerable.Range(0, count).Select(i => Day.AddHours(fromHour).AddMinutes(i));

    private static byte[] Bitmap(IEnumerable<DateTime> minutes)
    {
        var bitmap = MinuteBitmap.Create();
        foreach (var minute in minutes)
            MinuteBitmap.Set(bitmap, (int)minute.TimeOfDay.TotalMinutes);
        return bitmap;
    }

    [Fact]
    public async Task Simultaneous_work_on_two_devices_is_counted_once()
    {
        using var db = new TestDb();
        var scope = new StatsScope();
        var service = new ComputerTimeService(db, scope);
        var laptop = Guid.NewGuid();

        // Тут 10:00–11:00, на ноутбуці 10:30–11:30: разом 1,5 год, а не 2
        await service.AddMinutesAsync(Minutes(10, 60));
        await service.AddMinutesAsync(Minutes(10, 30)); // повторні хвилини не додаються
        var laptopMinutes = Bitmap(Minutes(10, 60).Select(m => m.AddMinutes(30)));
        await new CloudSyncService(db).ApplyRemoteActivityAsync(new[] { (laptop, DateOnly.FromDateTime(Day), laptopMinutes) });

        Assert.Equal(TimeSpan.FromMinutes(90), await service.GetComputerTimeAsync(Day, Day));

        scope.SetThisDevice();
        Assert.Equal(TimeSpan.FromMinutes(60), await service.GetComputerTimeAsync(Day, Day));

        scope.SetRemoteDevice(laptop);
        Assert.Equal(TimeSpan.FromMinutes(60), await service.GetComputerTimeAsync(Day, Day));
    }

    [Fact]
    public async Task Range_sums_days()
    {
        using var db = new TestDb();
        var service = new ComputerTimeService(db, new StatsScope());

        await service.AddMinutesAsync(Minutes(9, 15));
        await service.AddMinutesAsync(Minutes(9, 20).Select(m => m.AddDays(1)));

        Assert.Equal(TimeSpan.FromMinutes(35), await service.GetComputerTimeAsync(Day, Day.AddDays(1)));
        Assert.Equal(TimeSpan.FromMinutes(15), await service.GetComputerTimeAsync(Day, Day));
    }
}
