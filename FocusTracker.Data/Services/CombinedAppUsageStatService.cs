using FocusTracker.Domain;
using FocusTracker.Domain.Interfaces;
using FocusTracker.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace FocusTracker.Data.Services
{
    // Статистика для екранів: локальні дані плюс завантажені з інших пристроїв,
    // залежно від вибраного StatsScope. Запис завжди йде в локальну базу.
    public class CombinedAppUsageStatService : IAppUsageStatService
    {
        private readonly AppUsageStatService _local;
        private readonly IDbContextFactory<FocusTrackerDbContext> _dbFactory;
        private readonly StatsScope _scope;

        public CombinedAppUsageStatService(AppUsageStatService local, IDbContextFactory<FocusTrackerDbContext> dbFactory, StatsScope scope)
        {
            _local = local;
            _dbFactory = dbFactory;
            _scope = scope;
        }

        public Task AddOrUpdateUsageAsync(string appName, TimeSpan totalTime, TimeSpan activeTime) =>
            _local.AddOrUpdateUsageAsync(appName, totalTime, activeTime);

        public Task RecalculateAppUsageStatsForDateAsync(DateTime date) => _local.RecalculateAppUsageStatsForDateAsync(date);

        public Task RecalculateTodayAsync() => _local.RecalculateTodayAsync();

        public Task<List<AppUsageStat>> GetStatsForTodayAsync() => GetStatsForDayAsync(DateTime.Today);

        public async Task<List<AppUsageStat>> GetStatsForDayAsync(DateTime date)
        {
            var local = _scope.IncludesLocal ? await _local.GetStatsForDayAsync(date) : new List<AppUsageStat>();
            return MergeDaily(local, await GetRemoteAsync(date, date));
        }

        public async Task<IEnumerable<AppUsageStat>> GetStatsInRangeAsync(DateTime from, DateTime to)
        {
            var local = _scope.IncludesLocal ? (await _local.GetStatsInRangeAsync(from, to)).ToList() : new List<AppUsageStat>();
            return MergeDaily(local, await GetRemoteAsync(from, to));
        }

        public async Task<List<HourlyAppUsageLog>> GetHourlyStatsAsync(DateTime date)
        {
            var local = _scope.IncludesLocal ? await _local.GetHourlyStatsAsync(date) : new List<HourlyAppUsageLog>();
            var remote = (await GetRemoteAsync(date, date)).Select(r => new HourlyAppUsageLog
            {
                AppName = r.AppName,
                Date = r.Date,
                Hour = r.Hour,
                TotalTime = r.TotalTime,
                ActiveTime = r.ActiveTime
            });

            return local.Concat(remote)
                .GroupBy(h => (h.AppName, h.Hour))
                .Select(g => new HourlyAppUsageLog
                {
                    AppName = g.Key.AppName,
                    Date = date,
                    Hour = g.Key.Hour,
                    TotalTime = Sum(g.Select(h => h.TotalTime)),
                    ActiveTime = Sum(g.Select(h => h.ActiveTime))
                })
                .ToList();
        }

        public Task<IEnumerable<HourlyCategoryStat>> GetHourlyCategoryStatsForTodayAsync() =>
            GetHourlyCategoryStatsForDateAsync(DateTime.Today);

        public async Task<IEnumerable<HourlyCategoryStat>> GetHourlyCategoryStatsForDateAsync(DateTime date)
        {
            var hourly = await GetHourlyStatsAsync(date);

            await using var db = await _dbFactory.CreateDbContextAsync();
            var categories = await db.TrackedPrograms.ToDictionaryAsync(p => p.Identifier, p => p.Category);
            return AppUsageStatService.BuildHourlyCategoryStats(hourly, categories);
        }

        public async Task<DateTime> GetMinTrackedDateAsync()
        {
            var local = _scope.IncludesLocal ? await _local.GetMinTrackedDateAsync() : DateTime.Today;

            await using var db = await _dbFactory.CreateDbContextAsync();
            var remote = await FilterRemote(db.RemoteHourlyUsages)
                .OrderBy(r => r.Date)
                .Select(r => (DateTime?)r.Date)
                .FirstOrDefaultAsync();

            return remote.HasValue && remote.Value < local ? remote.Value : local;
        }

        private async Task<List<RemoteHourlyUsage>> GetRemoteAsync(DateTime from, DateTime to)
        {
            if (_scope.Kind == StatsScopeKind.ThisDevice)
                return new List<RemoteHourlyUsage>();

            await using var db = await _dbFactory.CreateDbContextAsync();
            return await FilterRemote(db.RemoteHourlyUsages)
                .Where(r => r.Date >= from.Date && r.Date <= to.Date)
                .AsNoTracking()
                .ToListAsync();
        }

        private IQueryable<RemoteHourlyUsage> FilterRemote(IQueryable<RemoteHourlyUsage> query) => _scope.Kind switch
        {
            StatsScopeKind.ThisDevice => query.Where(_ => false),
            StatsScopeKind.RemoteDevice => query.Where(r => r.DeviceId == _scope.RemoteDeviceId),
            _ => query
        };

        private static List<AppUsageStat> MergeDaily(IEnumerable<AppUsageStat> local, IEnumerable<RemoteHourlyUsage> remote)
        {
            var remoteDaily = remote.Select(r => new AppUsageStat
            {
                AppName = r.AppName,
                Date = r.Date,
                TotalTime = r.TotalTime,
                ActiveTime = r.ActiveTime
            });

            return local.Concat(remoteDaily)
                .GroupBy(s => (s.AppName, s.Date.Date))
                .Select(g => new AppUsageStat
                {
                    AppName = g.Key.AppName,
                    Date = g.Key.Item2,
                    TotalTime = Sum(g.Select(s => s.TotalTime)),
                    ActiveTime = Sum(g.Select(s => s.ActiveTime))
                })
                .ToList();
        }

        private static TimeSpan Sum(IEnumerable<TimeSpan> values) =>
            values.Aggregate(TimeSpan.Zero, (acc, value) => acc + value);
    }
}
