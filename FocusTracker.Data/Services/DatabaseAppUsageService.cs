using FocusTracker.Domain.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FocusTracker.Data.Services
{
    public class AppUsageService
    {
        private readonly FocusTrackerDbContext _context;

        public AppUsageService(FocusTrackerDbContext context)
        {
            _context = context;
        }

        public void AddOrUpdateUsage(string appName, TimeSpan duration)
        {
            var today = DateTime.Today;
            var stat = _context.AppUsageStats
                .FirstOrDefault(s => s.AppName == appName && s.Date == today);

            if (stat == null)
            {
                stat = new AppUsageStat
                {
                    AppName = appName,
                    Date = today,
                    TotalTime = duration
                };
                _context.AppUsageStats.Add(stat);
            }
            else
            {
                stat.TotalTime += duration;
                _context.AppUsageStats.Update(stat);
            }

            _context.SaveChanges();
        }

        public List<AppUsageStat> GetTodayStats()
        {
            var today = DateTime.Today;
            return _context.AppUsageStats
                .Where(s => s.Date == today)
                .OrderByDescending(s => s.TotalTime)
                .ToList();
        }
    }
}
