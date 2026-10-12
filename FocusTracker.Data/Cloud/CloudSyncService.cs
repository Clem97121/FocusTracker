using System.Globalization;
using FocusTracker.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace FocusTracker.Data.Cloud
{
    public record SyncResult(int Uploaded, int Downloaded);

    // Обмін почасовою статистикою з хмарною базою: свої зміни — туди,
    // рядки інших пристроїв — у локальну таблицю RemoteHourlyUsages
    public partial class CloudSyncService
    {
        public const int UploadBatchSize = 500;

        private const string UploadWatermarkKey = "cloud.upload_watermark";
        private const string DownloadWatermarkKey = "cloud.download_watermark";
        private const string SchemaReadyKey = "cloud.schema_ready_for";
        private const string ProgramsUploadWatermarkKey = "cloud.programs_upload_watermark";
        private const string ProgramsDownloadWatermarkKey = "cloud.programs_download_watermark";

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
                uploaded += await UploadProgramsAsync(cloud, deviceId, token);
                downloaded += await DownloadProgramsAsync(cloud, deviceId, token);
                await SyncIconsAsync(cloud, token);
                uploaded += await UploadEntitiesAsync(cloud, deviceId, token);
                downloaded += await DownloadEntitiesAsync(cloud, deviceId, token);
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
            var marker = $"{deviceId}:{CloudStore.SchemaVersion}";
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

        private async Task<int> UploadProgramsAsync(CloudStore cloud, Guid deviceId, CancellationToken token)
        {
            var startedAt = DateTime.UtcNow;
            var watermark = ParseDate(await GetStateAsync(ProgramsUploadWatermarkKey));

            List<TrackedProgram> changed;
            await using (var db = await _dbFactory.CreateDbContextAsync(token))
            {
                var query = db.TrackedPrograms.AsQueryable();
                if (watermark != null)
                    query = query.Where(p => p.SettingsUpdatedAt > watermark.Value);
                changed = await query.ToListAsync(token);

                // Уперше відправляємо всі програми. Налаштовані до появи синхронізації (не за замовчуванням)
                // отримують поточний час і перемагають; решта — найстаршу дату, щоб не перетерти хмарні
                if (watermark == null)
                {
                    foreach (var program in changed.Where(p => p.SettingsUpdatedAt == null && !HasDefaultSettings(p)))
                        program.SettingsUpdatedAt = startedAt;
                    await db.SaveChangesAsync(token);
                }
            }

            var settings = changed.Select(p => new CloudProgramSettings(
                p.Identifier, p.Category, p.IsTracked, p.IsHidden, p.IgnoreIdle,
                p.SettingsUpdatedAt ?? DateTime.UnixEpoch, deviceId)).ToList();
            foreach (var batch in settings.Chunk(UploadBatchSize))
                await cloud.UploadProgramsAsync(deviceId, batch, token);

            await SetStateAsync(ProgramsUploadWatermarkKey, FormatDate(startedAt));
            return changed.Count;
        }

        // Так програма виглядає, коли її щойно знайшов трекер
        private static bool HasDefaultSettings(TrackedProgram program) =>
            program.Category == "Суміжні" && program.IsTracked && !program.IsHidden && !program.IgnoreIdle;

        private async Task<int> DownloadProgramsAsync(CloudStore cloud, Guid deviceId, CancellationToken token)
        {
            var watermark = ParseDate(await GetStateAsync(ProgramsDownloadWatermarkKey));
            var since = watermark.HasValue ? DateTime.SpecifyKind(watermark.Value, DateTimeKind.Utc) - DownloadOverlap : (DateTime?)null;

            var rows = await cloud.DownloadProgramsAsync(deviceId, since, token);
            if (rows.Count == 0) return 0;

            await ApplyRemoteProgramsAsync(rows.Select(r => r.Settings).ToList(), token);
            await SetStateAsync(ProgramsDownloadWatermarkKey, FormatDate(rows.Max(r => r.SyncedAtUtc)));
            return rows.Count;
        }

        // Налаштування з іншого пристрою застосовуються, якщо вони новіші за локальні
        public async Task ApplyRemoteProgramsAsync(IReadOnlyCollection<CloudProgramSettings> programs, CancellationToken token = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(token);
            var local = (await db.TrackedPrograms.ToListAsync(token))
                .GroupBy(p => p.Identifier)
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var remote in programs)
            {
                if (!local.TryGetValue(remote.Identifier, out var program))
                {
                    program = new TrackedProgram { Name = remote.Identifier, Identifier = remote.Identifier };
                    db.TrackedPrograms.Add(program);
                    local[remote.Identifier] = program;
                }
                else if (program.SettingsUpdatedAt != null && program.SettingsUpdatedAt >= remote.UpdatedAtUtc)
                {
                    continue;
                }

                program.Category = remote.Category;
                program.IsTracked = remote.IsTracked;
                program.IsHidden = remote.IsHidden;
                program.IgnoreIdle = remote.IgnoreIdle;
                program.SettingsUpdatedAt = remote.UpdatedAtUtc;
            }

            await db.SaveChangesAsync(token);
        }

        public const int IconBatchSize = 50;

        // Свої іконки, яких немає в хмарі, — туди; іконки програм, яких тут немає локально, — сюди
        private async Task SyncIconsAsync(CloudStore cloud, CancellationToken token)
        {
            var inCloud = await cloud.GetIconIdentifiersAsync(token);

            await using var db = await _dbFactory.CreateDbContextAsync(token);
            var programs = await db.TrackedPrograms.ToListAsync(token);

            var toUpload = programs
                .Where(p => p.IconBytes is { Length: > 0 } && !inCloud.Contains(p.Identifier))
                .GroupBy(p => p.Identifier)
                .Select(g => (g.Key, g.First().IconBytes!))
                .ToList();
            foreach (var batch in toUpload.Chunk(IconBatchSize))
                await cloud.UploadIconsAsync(batch, token);

            var missing = programs
                .Where(p => p.IconBytes is not { Length: > 0 } && inCloud.Contains(p.Identifier))
                .ToList();
            if (missing.Count == 0) return;

            var icons = (await cloud.DownloadIconsAsync(missing.Select(p => p.Identifier).Distinct().ToList(), token))
                .ToDictionary(i => i.Identifier, i => i.Icon);
            foreach (var program in missing)
            {
                if (icons.TryGetValue(program.Identifier, out var icon))
                    program.IconBytes = icon;
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
