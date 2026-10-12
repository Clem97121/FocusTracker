using FocusTracker.Domain;
using FocusTracker.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace FocusTracker.Data.Services
{
    // «Час за комп'ютером»: хвилини, коли користувач був хоча б за одним пристроєм.
    // Якщо ПК і ноутбук працювали одночасно, ці хвилини рахуються один раз.
    public class ComputerTimeService
    {
        private readonly IDbContextFactory<FocusTrackerDbContext> _dbFactory;
        private readonly StatsScope _scope;

        public ComputerTimeService(IDbContextFactory<FocusTrackerDbContext> dbFactory, StatsScope scope)
        {
            _dbFactory = dbFactory;
            _scope = scope;
        }

        // Позначає хвилини присутності на цьому пристрої
        public async Task AddMinutesAsync(IEnumerable<DateTime> minutes)
        {
            var byDate = minutes.GroupBy(m => m.Date).ToList();
            if (byDate.Count == 0) return;

            await using var db = await _dbFactory.CreateDbContextAsync();
            var now = DateTime.Now;
            foreach (var group in byDate)
            {
                var day = await db.ActivityDays.FindAsync(group.Key);
                if (day == null)
                {
                    day = new ActivityDay { Date = group.Key };
                    db.ActivityDays.Add(day);
                }

                // Новий масив, щоб EF помітив зміну byte[]
                var bitmap = (byte[])day.Minutes.Clone();
                foreach (var minute in group)
                    MinuteBitmap.Set(bitmap, (int)minute.TimeOfDay.TotalMinutes);
                day.Minutes = bitmap;
                day.UpdatedAt = now;
            }
            await db.SaveChangesAsync();
        }

        // Час за комп'ютером за проміжок дат для вибраного StatsScope
        public async Task<TimeSpan> GetComputerTimeAsync(DateTime from, DateTime to)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var bitmaps = new List<(DateTime Date, byte[] Minutes)>();
            if (_scope.IncludesLocal)
            {
                bitmaps.AddRange((await db.ActivityDays.AsNoTracking()
                        .Where(a => a.Date >= from.Date && a.Date <= to.Date).ToListAsync())
                    .Select(a => (a.Date, a.Minutes)));
            }

            if (_scope.Kind != StatsScopeKind.ThisDevice)
            {
                var remote = db.RemoteActivityDays.AsNoTracking().Where(a => a.Date >= from.Date && a.Date <= to.Date);
                if (_scope.Kind == StatsScopeKind.RemoteDevice)
                    remote = remote.Where(a => a.DeviceId == _scope.RemoteDeviceId);
                bitmaps.AddRange((await remote.ToListAsync()).Select(a => (a.Date, a.Minutes)));
            }

            var minutes = bitmaps
                .GroupBy(b => b.Date.Date)
                .Sum(g => MinuteBitmap.Count(MinuteBitmap.Or(g.Select(b => b.Minutes))));
            return TimeSpan.FromMinutes(minutes);
        }
    }
}
