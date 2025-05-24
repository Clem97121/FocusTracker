using FocusTracker.Domain.Interfaces;
using FocusTracker.Domain.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace FocusTracker.App.Services
{
    public class RestrictionNotificationService : IRestrictionNotificationService
    {
        private readonly IRestrictionService _restrictionService;
        private readonly IAppUsageStatService _usageStatService;
        private readonly INotificationService _notifier;

        private readonly Dictionary<int, DateTime> _notified = new();

        public RestrictionNotificationService(
            IRestrictionService restrictionService,
            IAppUsageStatService usageStatService,
            INotificationService notifier)
        {
            _restrictionService = restrictionService;
            _usageStatService = usageStatService;
            _notifier = notifier;
        }

        public async Task CheckAndNotifyAsync()
        {
            var now = DateTime.Now;
            var nowTime = now.TimeOfDay;

            var activeProcess = GetActiveProcessName();
            var restrictions = await _restrictionService.GetAllAsync();
            var stats = await _usageStatService.GetStatsForTodayAsync();

            foreach (var restriction in restrictions)
            {
                foreach (var rule in restriction.Rules)
                {
                    if (rule.RuleType == "time_interval")
                    {
                        if (string.IsNullOrWhiteSpace(rule.Value)) continue;

                        var parts = rule.Value.Split('–');
                        if (parts.Length != 2 || !TimeSpan.TryParse(parts[0], out var from)) continue;

                        var timeLeft = from - nowTime;
                        if (timeLeft.TotalMinutes <= 5 && timeLeft.TotalMinutes > 0 && ShouldNotify(restriction.Id, now))
                        {
                            if (restriction.Targets.Any(t =>
                                t.Program?.Identifier?.Equals(activeProcess, StringComparison.OrdinalIgnoreCase) == true))
                            {
                                _notified[restriction.Id] = now;

                                var name = restriction.Targets
                                    .FirstOrDefault(t => t.Program?.Identifier == activeProcess)?
                                    .Program?.Name ?? activeProcess;

                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    _notifier.ShowMessage(
                                        $"\uD83D\uDD14 Програма {name} скоро буде заблокована (через {Math.Floor(timeLeft.TotalMinutes)} хв)");
                                });
                            }
                        }
                    }

                    else if (rule.RuleType == "max_total_seconds")
                    {
                        if (!int.TryParse(rule.Value, out int maxMinutes)) continue;

                        var usedMinutes = restriction.Targets
                            .Select(t => t.Program?.Identifier)
                            .Where(id => id != null)
                            .Select(id => stats.FirstOrDefault(s => s.AppName == id))
                            .Where(stat => stat != null)
                            .Sum(stat => stat.TotalTime.TotalMinutes);

                        var remaining = maxMinutes - usedMinutes;

                        if (remaining <= 10 && remaining > 0 && ShouldNotify(restriction.Id, now))
                        {
                            if (restriction.Targets.Any(t =>
                                t.Program?.Identifier?.Equals(activeProcess, StringComparison.OrdinalIgnoreCase) == true))
                            {
                                _notified[restriction.Id] = now;

                                var name = restriction.Targets
                                    .FirstOrDefault(t => t.Program?.Identifier == activeProcess)?
                                    .Program?.Name ?? activeProcess;

                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    _notifier.ShowMessage(
                                        $"\u23F3 Ліміт на {name} майже вичерпано: залишилось {Math.Floor(remaining)} хв");
                                });
                            }
                        }
                    }
                }
            }
        }

        private bool ShouldNotify(int restrictionId, DateTime now)
        {
            return !_notified.TryGetValue(restrictionId, out var lastTime) ||
                   lastTime.Date != now.Date ||
                   (now - lastTime).TotalMinutes >= 60;
        }

        private string GetActiveProcessName()
        {
            try
            {
                var hWnd = NativeMethods.GetForegroundWindow();
                NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
                var process = Process.GetProcessById((int)pid);
                return process.ProcessName;
            }
            catch
            {
                return null;
            }
        }

        private static class NativeMethods
        {
            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern IntPtr GetForegroundWindow();

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        }
    }
}
