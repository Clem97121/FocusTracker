namespace FocusTracker.Domain.Models;

// Хвилини доби, коли користувач був за цим пристроєм: 1440 біт (по біту на хвилину)
public class ActivityDay
{
    public DateTime Date { get; set; }
    public byte[] Minutes { get; set; } = MinuteBitmap.Create();
    public DateTime UpdatedAt { get; set; }
}

// Те саме для інших пристроїв, завантажене з хмарної бази
public class RemoteActivityDay
{
    public int Id { get; set; }
    public Guid DeviceId { get; set; }
    public DateTime Date { get; set; }
    public byte[] Minutes { get; set; } = MinuteBitmap.Create();
}

public static class MinuteBitmap
{
    public const int MinutesPerDay = 24 * 60;
    public const int Size = MinutesPerDay / 8;

    public static byte[] Create() => new byte[Size];

    public static void Set(byte[] bitmap, int minuteOfDay) =>
        bitmap[minuteOfDay / 8] |= (byte)(1 << (minuteOfDay % 8));

    // Об'єднання: хвилина зайнята, якщо вона зайнята хоча б на одному пристрої
    public static byte[] Or(IEnumerable<byte[]> bitmaps)
    {
        var result = Create();
        foreach (var bitmap in bitmaps)
        {
            for (int i = 0; i < Size && i < bitmap.Length; i++)
                result[i] |= bitmap[i];
        }
        return result;
    }

    public static int Count(byte[] bitmap) =>
        bitmap.Sum(b => System.Numerics.BitOperations.PopCount(b));
}
