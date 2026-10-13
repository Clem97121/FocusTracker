using FocusTracker.Mobile.Platforms.Android;

namespace FocusTracker.Mobile.Services;

// Відправляє статистику телефону в ті самі таблиці, що й ПК: телефон — ще один пристрій
public class PhoneSync
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly PhoneAccount _account = new();

    public static DateTime? LastSyncAt
    {
        get => Preferences.ContainsKey("sync.last") ? Preferences.Get("sync.last", DateTime.MinValue) : null;
        private set => Preferences.Set("sync.last", value ?? DateTime.MinValue);
    }

    public static string? LastError
    {
        get => Preferences.Get("sync.error", null as string);
        private set => Preferences.Set("sync.error", value);
    }

    public async Task<int> SyncAsync()
    {
        await Gate.WaitAsync();
        try
        {
            var context = global::Android.App.Application.Context;
            if (!_account.IsConfigured)
                throw new SupabaseException("Спершу увійдіть.");
            if (!UsageCollector.HasPermission(context))
                throw new SupabaseException("Немає доступу до історії використання.");

            // Абсолютні значення за вчора й сьогодні: повторна відправка нічого не подвоює
            var now = DateTime.Now;
            var snapshot = UsageCollector.Collect(context, DateTime.Today.AddDays(-1), now);

            var api = _account.Api();
            var token = await _account.GetAccessTokenAsync();
            var device = _account.DeviceId;
            var updatedAt = DateTime.UtcNow;

            await api.UpsertAsync("ft_devices", "device_id",
                new[] { new { device_id = device, name = _account.DeviceName, last_seen_at = updatedAt } }, token);

            if (snapshot.Hours.Count > 0)
            {
                await api.UpsertAsync("ft_hourly_usage", "device_id,app_name,date,hour", snapshot.Hours.Select(h => new
                {
                    device_id = device,
                    app_name = h.App,
                    date = h.Date.ToString("yyyy-MM-dd"),
                    hour = h.Hour,
                    total_seconds = h.Seconds,
                    // На телефоні програма на екрані — це завжди активне використання
                    active_seconds = h.Seconds,
                    updated_at = updatedAt,
                }).ToList(), token);
            }

            if (snapshot.Minutes.Count > 0)
            {
                await api.UpsertAsync("ft_activity", "device_id,date", snapshot.Minutes.Select(m => new
                {
                    device_id = device,
                    date = m.Key.ToString("yyyy-MM-dd"),
                    minutes = SupabaseApi.ToBytea(m.Value),
                    updated_at = updatedAt,
                }).ToList(), token);
            }

            await api.UpsertAsync("ft_presence", "device_id", new[]
            {
                new
                {
                    device_id = device,
                    name = _account.DeviceName,
                    last_input_at = snapshot.LastActivityUtc,
                    foreground_app = snapshot.ForegroundApp,
                    updated_at = updatedAt,
                }
            }, token);

            LastSyncAt = now;
            LastError = null;
            return snapshot.Hours.Count;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            throw;
        }
        finally
        {
            Gate.Release();
        }
    }
}
