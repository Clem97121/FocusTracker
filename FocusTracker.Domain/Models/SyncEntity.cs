namespace FocusTracker.Domain.Models;

// Сутність, що синхронізується між пристроями. Локальні Id різні на кожному пристрої,
// тож у хмарі сутність ідентифікує SyncId; SyncUpdatedAt (UTC) — час останньої зміни.
public interface ISyncEntity
{
    Guid SyncId { get; set; }
    DateTime? SyncUpdatedAt { get; set; }
}

// Слід видаленої сутності: щоб видалення дійшло до інших пристроїв
public class SyncTombstone
{
    public int Id { get; set; }
    public string Kind { get; set; } = string.Empty;
    public Guid SyncId { get; set; }
    public DateTime DeletedAt { get; set; }
}
