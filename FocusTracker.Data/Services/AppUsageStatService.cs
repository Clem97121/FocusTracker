using FocusTracker.Domain.Interfaces;
using FocusTracker.Domain.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FocusTracker.Data.Services
{
    public class AppUsageStatService : IAppUsageStatService
    {
        private readonly IDbContextFactory<FocusTrackerDbContext> _dbFactory;

        public AppUsageStatService(IDbContextFactory<FocusTrackerDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }
        public async Task AddOrUpdateUsageAsync(string appName, TimeSpan totalTime, TimeSpan activeTime)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var today = DateTime.Today;
            var now = DateTime.Now;
            var hour = now.Hour;

            var hourly = await db.HourlyAppUsageLogs
                .FirstOrDefaultAsync(h => h.AppName == appName && h.Date == today && h.Hour == hour);

            var stat = await db.AppUsageStats
                .FirstOrDefaultAsync(s => s.AppName == appName && s.Date == today);

            var tracked = await db.TrackedPrograms
                .FirstOrDefaultAsync(p => p.Identifier == appName);

            if (tracked == null)
            {
                tracked = new TrackedProgram
                {
                    Name = appName,
                    Identifier = appName,
                    Category = "Суміжні",
                    IsTracked = true,
                    IsHidden = false
                };
                db.TrackedPrograms.Add(tracked);
            }

            if (totalTime > TimeSpan.Zero)
                tracked.LastUsed = now;

            if (hourly == null)
            {
                hourly = new HourlyAppUsageLog
                {
                    AppName = appName,
                    Date = today,
                    Hour = hour,
                    TotalTime = totalTime,
                    ActiveTime = activeTime
                };
                db.HourlyAppUsageLogs.Add(hourly);
            }
            else
            {
                hourly.TotalTime += totalTime;
                hourly.ActiveTime += activeTime;
            }

            if (stat == null)
            {
                stat = new AppUsageStat
                {
                    AppName = appName,
                    Date = today,
                    TotalTime = totalTime,
                    ActiveTime = activeTime
                };
                db.AppUsageStats.Add(stat);
            }
            else
            {
                stat.TotalTime += totalTime;
                stat.ActiveTime += activeTime;
                db.AppUsageStats.Update(stat);
            }

            await db.SaveChangesAsync();
        }
        public async Task<List<AppUsageStat>> GetStatsForTodayAsync()
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await db.AppUsageStats
                .Where(s => s.Date == DateTime.Today)
                .AsNoTracking()
                .ToListAsync();
        }
        public async Task<List<AppUsageStat>> GetStatsForDayAsync(DateTime date)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await db.AppUsageStats
                .Where(s => s.Date == date)
                .AsNoTracking()
                .ToListAsync();
        }
        public async Task<List<HourlyAppUsageLog>> GetHourlyStatsAsync(DateTime date)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await db.HourlyAppUsageLogs
                .Where(h => h.Date == date)
                .AsNoTracking()
                .ToListAsync();
        }
        public async Task<IEnumerable<HourlyCategoryStat>> GetHourlyCategoryStatsForTodayAsync()
        {
            return await GetHourlyCategoryStatsForDateInternalAsync(DateTime.Today);
        }
        public async Task<IEnumerable<HourlyCategoryStat>> GetHourlyCategoryStatsForDateAsync(DateTime date)
        {
            return await GetHourlyCategoryStatsForDateInternalAsync(date);
        }
        private async Task<IEnumerable<HourlyCategoryStat>> GetHourlyCategoryStatsForDateInternalAsync(DateTime date)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            // Загрузка данных в память
            var usageLogs = await db.HourlyAppUsageLogs
                .Where(log => log.Date == date)
                .ToListAsync();

            var programCategories = await db.TrackedPrograms
                .ToDictionaryAsync(p => p.Identifier, p => p.Category);

            var grouped = usageLogs
                .GroupBy(log => log.Hour)
                .Select(group =>
                {
                    var stat = new HourlyCategoryStat { Hour = group.Key };

                    foreach (var item in group)
                    {
                        var category = programCategories.TryGetValue(item.AppName, out var cat)
                            ? cat
                            : "Суміжні";

                        var minutes = item.TotalTime.TotalMinutes;

                        switch (category)
                        {
                            case "Продуктивні":
                                stat.ProductiveMinutes += minutes;
                                break;
                            case "Непродуктивні":
                                stat.UnproductiveMinutes += minutes;
                                break;
                            default:
                                stat.NeutralMinutes += minutes;
                                break;
                        }
                    }

                    return stat;
                })
                .OrderBy(stat => stat.Hour)
                .ToList();

            return grouped;
        }
        public async Task<DateTime> GetMinTrackedDateAsync()
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var first = await db.AppUsageStats.OrderBy(s => s.Date).FirstOrDefaultAsync();
            return first?.Date ?? DateTime.Today;
        }
        public async Task RecalculateAppUsageStatsForDateAsync(DateTime date)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            // Загружаем все почасовые логи за выбранную дату
            var hourlyLogs = await db.HourlyAppUsageLogs
                .Where(log => log.Date == date)
                .ToListAsync();

            if (hourlyLogs.Count == 0)
                return; // Нет данных — ничего не делаем

            // Группируем по AppName
            var grouped = hourlyLogs
                .GroupBy(log => log.AppName)
                .Select(g => new AppUsageStat
                {
                    AppName = g.Key,
                    Date = date,
                    TotalTime = TimeSpan.FromSeconds(g.Sum(x => x.TotalTime.TotalSeconds)),
                    ActiveTime = TimeSpan.FromSeconds(g.Sum(x => x.ActiveTime.TotalSeconds))
                })
                .ToList();

            // Удаляем старые дневные записи за эту дату
            var oldStats = await db.AppUsageStats
                .Where(s => s.Date == date)
                .ToListAsync();

            db.AppUsageStats.RemoveRange(oldStats);
            db.AppUsageStats.AddRange(grouped);

            await db.SaveChangesAsync();
        }
        public Task RecalculateTodayAsync() =>
                    RecalculateAppUsageStatsForDateAsync(DateTime.Today);
        public async Task<IEnumerable<AppUsageStat>> GetStatsInRangeAsync(DateTime from, DateTime to)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await db.AppUsageStats
                .Where(s => s.Date >= from && s.Date <= to)
                .ToListAsync();
        }

    }
}
