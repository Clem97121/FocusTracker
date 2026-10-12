using System.Globalization;
using FocusTracker.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace FocusTracker.Data.Cloud
{
    public record SyncResult(int Uploaded, int Downloaded);

    // Обмін почасовою статистикою з хмарною базою: свої зміни — туди,
    // рядки інших пристроїв — у локальну таблицю RemoteHourlyUsages
    public class CloudSyncService
    {
        public const int UploadBatchSize = 500;

        private const string UploadWatermarkKey = "cloud.upload_watermark";
        private const string DownloadWatermarkKey = "cloud.download_watermark";
        private const string SchemaReadyKey = "cloud.schema_ready_for";

        // Транзакція на сервері може зафіксуватись пізніше за свій updated_at,
        // тож завантажуємо з запасом; повторні рядки просто перезапишуться
        private static readonly TimeSpan DownloadOverlap = TimeSpan.FromMinutes(2);

        private readonly IDbContextFactory<FocusTrackerDbContext> _dbFactory;
        private readonly SemaphoreSlim _gate = new(1, 1);

        public CloudSyncService(IDbContextFactory<FocusTrackerDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public async Task<SyncResult> SyncAsync(CloudStore cloud, Guid deviceId, string deviceName, CancellationToken token = default)
        {
            await _gate.WaitAsync(token);
            try
            {
                await EnsureSchemaAsync(cloud, deviceId, token);
                await cloud.UpsertDeviceAsync(deviceId, deviceName, token);

                var uploaded = await UploadAsync(cloud, deviceId, token);
                var downloaded = await DownloadAsync(cloud, deviceId, token);
                await RefreshDevicesAsync(cloud, deviceId, token);

                return new SyncResult(uploaded, downloaded);
            }
            finally
            {
                _gate.Release();
            }
        }

        // Перевірка підключення для сторінки налаштувань
        public static async Task TestConnectionAsync(CloudStore cloud, CancellationToken token = default)
        {
            await cloud.EnsureSchemaAsync(token);
            await cloud.GetDevicesAsync(token);
        }

        public async Task<List<RemoteDevice>> GetRemoteDevicesAsync()
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await db.RemoteDevices.AsNoTracking().OrderBy(d => d.Name).ToListAsync();
        }

        // Після зміни бази (інший рядок підключення) все треба відправити й отримати заново
        public async Task ResetAsync()
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            db.SyncStates.RemoveRange(db.SyncStates);
            db.RemoteHourlyUsages.RemoveRange(db.RemoteHourlyUsages);
            db.RemoteDevices.RemoveRange(db.RemoteDevices);
            await db.SaveChangesAsync();
        }

        private async Task EnsureSchemaAsync(CloudStore cloud, Guid deviceId, CancellationToken token)
        {
            var marker = deviceId.ToString();
            if (await GetStateAsync(SchemaReadyKey) == marker) return;

            await cloud.EnsureSchemaAsync(token);
            await SetStateAsync(SchemaReadyKey, marker);
        }

        private async Task<int> UploadAsync(CloudStore cloud, Guid deviceId, CancellationToken token)
        {
            // Мітку беремо до вибірки: рядки, змінені під час відправки, підуть наступного разу
            var startedAt = DateTime.Now;
            var watermark = ParseDate(await GetStateAsync(UploadWatermarkKey));

            List<HourlyAppUsageLog> changed;
            await using (var db = await _dbFactory.CreateDbContextAsync(token))
            {
                var query = db.HourlyAppUsageLogs.AsNoTracking();
                if (watermark != null)
                    query = query.Where(h => h.UpdatedAt > watermark.Value);
                changed = await query.ToListAsync(token);
            }

            foreach (var batch in changed.Chunk(UploadBatchSize))
            {
                var rows = batch.Select(h => new CloudHourlyRow(
                    deviceId, h.AppName, DateOnly.FromDateTime(h.Date), h.Hour,
                    h.TotalTime.TotalSeconds, h.ActiveTime.TotalSeconds, default)).ToList();
                await cloud.UploadHourlyAsync(deviceId, rows, token);
            }

            await SetStateAsync(UploadWatermarkKey, FormatDate(startedAt));
            return changed.Count;
        }

        private async Task<int> DownloadAsync(CloudStore cloud, Guid deviceId, CancellationToken token)
        {
            var watermark = ParseDate(await GetStateAsync(DownloadWatermarkKey));
            var since = watermark.HasValue ? DateTime.SpecifyKind(watermark.Value, DateTimeKind.Utc) - DownloadOverlap : (DateTime?)null;

            var rows = await cloud.DownloadHourlyAsync(deviceId, since, token);
            if (rows.Count == 0) return 0;

            await ApplyRemoteRowsAsync(rows, token);
            await SetStateAsync(DownloadWatermarkKey, FormatDate(rows.Max(r => r.UpdatedAtUtc)));
            return rows.Count;
        }

        // Записує рядки інших пристроїв у локальну копію і додає нові програми до списку застосунків
        public async Task ApplyRemoteRowsAsync(IReadOnlyCollection<CloudHourlyRow> rows, CancellationToken token = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(token);

            foreach (var group in rows.GroupBy(r => (r.DeviceId, r.Date)))
            {
                var date = group.Key.Date.ToDateTime(TimeOnly.MinValue);
                var existing = await db.RemoteHourlyUsages
                    .Where(r => r.DeviceId == group.Key.DeviceId && r.Date == date)
                    .ToDictionaryAsync(r => (r.AppName, r.Hour), token);

                foreach (var row in group)
                {
                    if (!existing.TryGetValue((row.AppName, row.Hour), out var local))
                    {
                        local = new RemoteHourlyUsage
                        {
                            DeviceId = row.DeviceId,
                            AppName = row.AppName,
                            Date = date,
                            Hour = row.Hour
                        };
                        db.RemoteHourlyUsages.Add(local);
                        existing[(row.AppName, row.Hour)] = local;
                    }

                    local.TotalTime = TimeSpan.FromSeconds(row.TotalSeconds);
                    local.ActiveTime = TimeSpan.FromSeconds(row.ActiveSeconds);
                }
            }

            var known = (await db.TrackedPrograms.Select(p => p.Identifier).ToListAsync(token))
                .ToHashSet(StringComparer.Ordinal);
            foreach (var appName in rows.Select(r => r.AppName).Distinct())
            {
                if (known.Contains(appName)) continue;
                db.TrackedPrograms.Add(new TrackedProgram
                {
                    Name = appName,
                    Identifier = appName,
                    Category = "Суміжні",
                    IsTracked = true
                });
            }

            await db.SaveChangesAsync(token);
        }

        private async Task RefreshDevicesAsync(CloudStore cloud, Guid deviceId, CancellationToken token)
        {
            var devices = await cloud.GetDevicesAsync(token);

            await using var db = await _dbFactory.CreateDbContextAsync(token);
            var existing = await db.RemoteDevices.ToDictionaryAsync(d => d.DeviceId, token);
            foreach (var device in devices.Where(d => d.DeviceId != deviceId))
            {
                if (!existing.TryGetValue(device.DeviceId, out var local))
                {
                    local = new RemoteDevice { DeviceId = device.DeviceId };
                    db.RemoteDevices.Add(local);
                }
                local.Name = device.Name;
                local.LastSeenAt = device.LastSeenAtUtc.ToLocalTime();
            }
            await db.SaveChangesAsync(token);
        }

        private async Task<string?> GetStateAsync(string key)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return (await db.SyncStates.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key))?.Value;
        }

        private async Task SetStateAsync(string key, string value)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var state = await db.SyncStates.FirstOrDefaultAsync(s => s.Key == key);
            if (state == null)
                db.SyncStates.Add(new SyncState { Key = key, Value = value });
            else
                state.Value = value;
            await db.SaveChangesAsync();
        }

        private static string FormatDate(DateTime value) => value.Ticks.ToString(CultureInfo.InvariantCulture);

        private static DateTime? ParseDate(string? value) =>
            long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks) ? new DateTime(ticks) : null;
    }
}
