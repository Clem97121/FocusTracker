using FocusTracker.App.Helpers;
using FocusTracker.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FocusTracker.App.Services
{
    // Накопичує час використання програм у пам'яті (щосекунди)
    // і раз на FlushInterval записує його в БД та перевіряє обмеження.
    public class UsageRecorder
    {
        public static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(5);

        private readonly IServiceProvider _services;
        private readonly object _lock = new();
        private readonly SemaphoreSlim _flushGate = new(1, 1);
        private Dictionary<string, (TimeSpan Total, TimeSpan Active)> _pending = new();
        private readonly Dictionary<string, DateTime> _violationCache = new();

        public UsageRecorder(IServiceProvider services)
        {
            _services = services;
        }

        public void Add(string appName, TimeSpan total, TimeSpan active)
        {
            if (string.IsNullOrWhiteSpace(appName)) return;

            lock (_lock)
            {
                _pending.TryGetValue(appName, out var current);
                _pending[appName] = (current.Total + total, current.Active + active);
            }
        }

        // Записує накопичене в БД. Виклики не накладаються.
        public async Task FlushAsync()
        {
            await _flushGate.WaitAsync();
            try
            {
                Dictionary<string, (TimeSpan Total, TimeSpan Active)> batch;
                lock (_lock)
                {
                    if (_pending.Count == 0) return;
                    batch = _pending;
                    _pending = new();
                }

                var statService = _services.GetRequiredService<IAppUsageStatService>();
                foreach (var (appName, usage) in batch)
                {
                    try
                    {
                        await statService.AddOrUpdateUsageAsync(appName, usage.Total, usage.Active);
                    }
                    catch (Exception ex)
                    {
                        // Повертаємо в буфер, щоб не втратити час, і спробуємо наступного разу
                        Add(appName, usage.Total, usage.Active);
                        AppLog.Error($"Не вдалося записати статистику для {appName}", ex);
                    }
                }
            }
            finally
            {
                _flushGate.Release();
            }
        }

        // Перевіряє обмеження для програми у фокусі і закриває її, якщо ліміт порушено
        public async Task EnforceRestrictionsAsync(string appName)
        {
            if (string.IsNullOrWhiteSpace(appName) || appName == "Unknown") return;

            var statService = _services.GetRequiredService<IAppUsageStatService>();
            var evaluator = _services.GetRequiredService<IRestrictionEvaluator>();
            var notifier = _services.GetRequiredService<INotificationService>();

            var stats = await statService.GetStatsForTodayAsync();
            var total = stats.FirstOrDefault(s => s.AppName == appName)?.TotalTime ?? TimeSpan.Zero;

            if (!evaluator.TryGetViolatedRestrictions(appName, total, out var notes)) return;

            var processes = Process.GetProcessesByName(appName);
            foreach (var proc in processes)
            {
                try { proc.Kill(); }
                catch (Exception ex) { AppLog.Error($"Не вдалося закрити {appName}", ex); }
            }

            var cacheKey = $"{appName}_{string.Join(",", notes)}";
            if (_violationCache.TryGetValue(cacheKey, out var lastShown) && (DateTime.Now - lastShown).TotalSeconds < 30)
                return;

            _violationCache[cacheKey] = DateTime.Now;
            AppLog.Info($"Закрито {appName}: {string.Join("; ", notes)}");
            var message = $"Програму \"{appName}\" було закрито через перевищення обмеження:\n• {string.Join("\n• ", notes)}";
            notifier.ShowMessage(message, "Обмеження перевищено");
        }
    }
}
