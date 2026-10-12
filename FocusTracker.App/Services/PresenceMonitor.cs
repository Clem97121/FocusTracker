using FocusTracker.App.Helpers;
using FocusTracker.Core.Services;
using FocusTracker.Data;
using FocusTracker.Domain;
using FocusTracker.Domain.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FocusTracker.App.Services
{
    // Чи рахувати час програми у фокусі: користувач вводив щось, програма грає звук
    // або розгорнута на весь екран, чи позначена «не враховувати простій»
    public class PresenceMonitor
    {
        // Звук і повний екран перевіряємо лише коли введення давно не було, щоб не опитувати аудіо щосекунди
        private static readonly TimeSpan MediaCheckAfter = TimeSpan.FromSeconds(30);

        private readonly UserActivityTracker _userActivity;
        private readonly MediaActivityDetector _media = new();
        private readonly IdleDetector _idle;
        private readonly IProgramService _programService;
        private volatile HashSet<string> _ignoreIdle = new(StringComparer.OrdinalIgnoreCase);
        private bool _mediaErrorLogged;

        public PresenceMonitor(UserActivityTracker userActivity, IProgramService programService, SettingsStore settings)
        {
            _userActivity = userActivity;
            _programService = programService;
            _idle = new IdleDetector(DateTime.Now, TimeSpan.FromMinutes(settings.Current.IdleThresholdMinutes));
            settings.Changed += s => _idle.Threshold = TimeSpan.FromMinutes(s.IdleThresholdMinutes);
        }

        public bool IsPresent(string appName)
        {
            var now = DateTime.Now;
            _idle.Signal(_userActivity.LastInputTime);

            if (now - _idle.LastSignal > MediaCheckAfter && IsMediaActive(appName))
                _idle.Signal(now);

            return _idle.IsPresent(now, _ignoreIdle.Contains(appName));
        }

        public async Task RefreshIgnoreIdleAsync()
        {
            _ignoreIdle = await _programService.GetIgnoreIdleIdentifiersAsync();
        }

        private bool IsMediaActive(string appName)
        {
            try
            {
                return _media.IsMediaActive(appName);
            }
            catch (Exception ex)
            {
                // Пишемо в лог один раз, інакше помилка повторювалась би щосекунди
                if (!_mediaErrorLogged)
                {
                    _mediaErrorLogged = true;
                    AppLog.Error("Не вдалося перевірити звук або повноекранний режим", ex);
                }
                return false;
            }
        }
    }
}
