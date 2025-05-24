using FocusTracker.App.Helpers; // здесь должен быть метод ToReadableString()
using FocusTracker.Domain.Interfaces;
using FocusTracker.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;

namespace FocusTracker.App.Converters
{
    public class RestrictionDescriptionConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not RestrictionRule rule)
                return null;

            var usageService = App.Services.GetService<IAppUsageStatService>();
            if (usageService == null)
                return null;

            var statsTask = Task.Run(() => usageService.GetStatsForTodayAsync());
            statsTask.Wait();
            var stats = statsTask.Result.ToList();

            switch (rule.RuleType)
            {
                case "max_total_seconds":
                    if (int.TryParse(rule.Value, out int maxMinutes))
                    {
                        var programIds = rule.Restriction.Targets
                            .Select(t => t.Program?.Identifier)
                            .Where(i => i != null);

                        double usedMinutes = stats
                            .Where(s => programIds.Contains(s.AppName))
                            .Sum(s => s.TotalTime.TotalMinutes);

                        var left = Math.Max(0, maxMinutes - usedMinutes);
                        return $"Залишилось {TimeSpan.FromMinutes(left).ToReadableString()}";
                    }
                    break;

                case "time_interval":
                    var parts = rule.Value.Split('–');
                    if (parts.Length == 2 &&
                        TimeSpan.TryParse(parts[1], out TimeSpan toTime))
                    {
                        var now = DateTime.Now.TimeOfDay;
                        if (now > toTime)
                            return "Обмеження завершено";

                        var left = toTime - now;
                        return $"Залишилось {left.ToReadableString()}";
                    }
                    break;

                case "after_task":
                    if (int.TryParse(rule.Value, out int afterMinutes))
                    {
                        return $"Потрібно {TimeSpan.FromMinutes(afterMinutes).ToReadableString()} активного часу після задач";
                    }
                    break;
            }

            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
