using FocusTracker.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace FocusTracker.Data.Cloud
{
    // Синхронізація хвилин присутності для «часу за комп'ютером»
    public partial class CloudSyncService
    {
        private const string ActivityUploadWatermarkKey = "cloud.activity_upload_watermark";
        private const string ActivityDownloadWatermarkKey = "cloud.activity_download_watermark";

        private async Task<int> UploadActivityAsync(CloudStore cloud, Guid deviceId, CancellationToken token)
        {
            var startedAt = DateTime.Now;
            var watermark = ParseDate(await GetStateAsync(ActivityUploadWatermarkKey));

            List<ActivityDay> changed;
            await using (var db = await _dbFactory.CreateDbContextAsync(token))
            {
                var query = db.ActivityDays.AsNoTracking();
                if (watermark != null)
                    query = query.Where(a => a.UpdatedAt > watermark.Value);
                changed = await query.ToListAsync(token);
            }

            foreach (var batch in changed.Chunk(UploadBatchSize))
                await cloud.UploadActivityAsync(deviceId, batch.Select(a => (DateOnly.FromDateTime(a.Date), a.Minutes)).ToList(), token);

            await SetStateAsync(ActivityUploadWatermarkKey, FormatDate(startedAt));
            return changed.Count;
        }

        private async Task<int> DownloadActivityAsync(CloudStore cloud, Guid deviceId, CancellationToken token)
        {
            var watermark = ParseDate(await GetStateAsync(ActivityDownloadWatermarkKey));
            var since = watermark.HasValue ? DateTime.SpecifyKind(watermark.Value, DateTimeKind.Utc) - DownloadOverlap : (DateTime?)null;

            var rows = await cloud.DownloadActivityAsync(deviceId, since, token);
            if (rows.Count == 0) return 0;

            await ApplyRemoteActivityAsync(rows.Select(r => (r.DeviceId, r.Date, r.Minutes)).ToList(), token);
            await SetStateAsync(ActivityDownloadWatermarkKey, FormatDate(rows.Max(r => r.UpdatedAtUtc)));
            return rows.Count;
        }

        public async Task ApplyRemoteActivityAsync(IReadOnlyCollection<(Guid DeviceId, DateOnly Date, byte[] Minutes)> days, CancellationToken token = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(token);
            foreach (var (deviceId, date, minutes) in days)
            {
                var day = date.ToDateTime(TimeOnly.MinValue);
                var local = await db.RemoteActivityDays.FirstOrDefaultAsync(a => a.DeviceId == deviceId && a.Date == day, token);
                if (local == null)
                {
                    local = new RemoteActivityDay { DeviceId = deviceId, Date = day };
                    db.RemoteActivityDays.Add(local);
                }
                local.Minutes = minutes;
            }
            await db.SaveChangesAsync(token);
        }
    }
}
