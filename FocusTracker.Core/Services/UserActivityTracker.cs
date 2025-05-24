using System;
using System.Timers;
using Gma.System.MouseKeyHook;

namespace FocusTracker.Core.Services
{
    public class UserActivityTracker
    {
        private readonly System.Timers.Timer _checkTimer;
        private DateTime _lastInputTime;
        private TimeSpan _activeTime;
        private IKeyboardMouseEvents _events;

        public event Action<TimeSpan> OnActiveTimeUpdated;
        public UserActivityTracker(double interval = 5000) // Проверка каждые 5 секунд
        {
            _checkTimer = new System.Timers.Timer(interval);
            _checkTimer.Elapsed += CheckInactivity;
        }

        public void Start()
        {
            _lastInputTime = DateTime.Now;
            _activeTime = TimeSpan.Zero;

            _events = Hook.GlobalEvents();
            _events.MouseMove += OnUserActivity;
            _events.KeyDown += OnUserActivity;

            _checkTimer.Start();
        }

        public void Stop()
        {
            _checkTimer.Stop();
            _events.MouseMove -= OnUserActivity;
            _events.KeyDown -= OnUserActivity;
            _events.Dispose();
        }

        private void OnUserActivity(object sender, EventArgs e)
        {
            _lastInputTime = DateTime.Now;
        }

        private void CheckInactivity(object sender, ElapsedEventArgs e)
        {
            if ((DateTime.Now - _lastInputTime).TotalSeconds < 5)
            {
                _activeTime += TimeSpan.FromSeconds(5);
                OnActiveTimeUpdated?.Invoke(_activeTime);
            }
        }

        public TimeSpan GetAndResetActiveTime()
        {
            var result = _activeTime;
            _activeTime = TimeSpan.Zero;
            return result;
        }
    }
}
