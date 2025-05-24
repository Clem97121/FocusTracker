using System;
using System.Linq;
using System.Threading.Tasks;
using FocusTracker.Domain.Models;

namespace FocusTracker.Domain.Interfaces
{
    public interface IAppUsageStatService
    {
        Task AddOrUpdateUsageAsync(string appName, TimeSpan totalTime, TimeSpan activeTime);
        Task<IQueryable<AppUsageStat>> GetStatsForTodayAsync();
        Task<IQueryable<AppUsageStat>> GetStatsForDayAsync(DateTime date);
        Task<IQueryable<HourlyAppUsageLog>> GetHourlyStatsAsync(DateTime date);
        Task<IEnumerable<HourlyCategoryStat>> GetHourlyCategoryStatsForTodayAsync();
        Task<IEnumerable<HourlyCategoryStat>> GetHourlyCategoryStatsForDateAsync(DateTime date);

        // ➕ Вот этот метод добавь
        Task<DateTime> GetMinTrackedDateAsync();
    }

}
