using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FocusTracker.Domain.Models;

namespace FocusTracker.Domain.Interfaces
{
    public interface IAppUsageStatService
    {
        Task AddOrUpdateUsageAsync(string appName, TimeSpan totalTime, TimeSpan activeTime);

        Task<List<AppUsageStat>> GetStatsForTodayAsync();
        Task<List<AppUsageStat>> GetStatsForDayAsync(DateTime date);
        Task<List<HourlyAppUsageLog>> GetHourlyStatsAsync(DateTime date);

        Task<IEnumerable<HourlyCategoryStat>> GetHourlyCategoryStatsForTodayAsync();
        Task<IEnumerable<HourlyCategoryStat>> GetHourlyCategoryStatsForDateAsync(DateTime date);

        Task<DateTime> GetMinTrackedDateAsync();

        // Добавленные ранее методы пересчета
        Task RecalculateAppUsageStatsForDateAsync(DateTime date);
        Task RecalculateTodayAsync();

        Task<IEnumerable<AppUsageStat>> GetStatsInRangeAsync(DateTime from, DateTime to);

    }
}
