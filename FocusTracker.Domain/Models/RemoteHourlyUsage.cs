namespace FocusTracker.Domain.Models;

// Почасова статистика інших пристроїв, завантажена з хмарної бази
public class RemoteHourlyUsage
{
    public int Id { get; set; }
    public Guid DeviceId { get; set; }
    public string AppName { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public int Hour { get; set; }
    public TimeSpan TotalTime { get; set; }
    public TimeSpan ActiveTime { get; set; }
}

// Інші пристрої, відомі з хмарної бази
public class RemoteDevice
{
    public Guid DeviceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime LastSeenAt { get; set; }
}

// Службовий стан синхронізації (ключ — значення)
public class SyncState
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
