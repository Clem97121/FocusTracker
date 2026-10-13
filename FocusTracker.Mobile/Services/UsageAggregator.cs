namespace FocusTracker.Mobile.Services;

public record UsageInterval(string App, DateTime StartLocal, DateTime EndLocal);

public record HourUsage(string App, DateOnly Date, int Hour, double Seconds);

// Packages — назва програми (як у статистиці) → пакет Android, з якого беремо іконку
public record UsageSnapshot(
    List<HourUsage> Hours,
    Dictionary<DateOnly, byte[]> Minutes,
    DateTime? LastActivityUtc,
    string? ForegroundApp,
    Dictionary<string, string> Packages);

// Інтервали «програма була на екрані» → секунди по годинах і хвилини присутності за добу,
// у тих самих форматах, що й у ПК (ft_hourly_usage, ft_activity)
public static class UsageAggregator
{
    public const int MinutesPerDay = 24 * 60;

    public static (List<HourUsage> Hours, Dictionary<DateOnly, byte[]> Minutes) Aggregate(IEnumerable<UsageInterval> intervals)
    {
        var seconds = new Dictionary<(string App, DateOnly Date, int Hour), double>();
        var minutes = new Dictionary<DateOnly, byte[]>();

        foreach (var interval in intervals)
        {
            var start = interval.StartLocal;
            while (start < interval.EndLocal)
            {
                // Розбиваємо інтервал на шматки в межах однієї години
                var hourEnd = new DateTime(start.Year, start.Month, start.Day, start.Hour, 0, 0).AddHours(1);
                var end = interval.EndLocal < hourEnd ? interval.EndLocal : hourEnd;
                var key = (interval.App, DateOnly.FromDateTime(start), start.Hour);
                seconds[key] = seconds.GetValueOrDefault(key) + (end - start).TotalSeconds;
                MarkMinutes(minutes, start, end);
                start = end;
            }
        }

        var hours = seconds
            .Select(p => new HourUsage(p.Key.App, p.Key.Date, p.Key.Hour, Math.Round(p.Value, 1)))
            .Where(h => h.Seconds >= 1)
            .ToList();
        return (hours, minutes);
    }

    private static void MarkMinutes(Dictionary<DateOnly, byte[]> minutes, DateTime start, DateTime end)
    {
        var date = DateOnly.FromDateTime(start);
        if (!minutes.TryGetValue(date, out var bitmap))
            minutes[date] = bitmap = new byte[MinutesPerDay / 8];

        var first = (int)start.TimeOfDay.TotalMinutes;
        // Хвилина рахується, якщо в ній була хоч частина інтервалу
        var last = (int)Math.Ceiling(end.TimeOfDay.TotalMinutes) - 1;
        if (end.TimeOfDay == TimeSpan.Zero) last = MinutesPerDay - 1;
        for (var m = first; m <= Math.Min(last, MinutesPerDay - 1); m++)
            bitmap[m / 8] |= (byte)(1 << (m % 8));
    }
}
