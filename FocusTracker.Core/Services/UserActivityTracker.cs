using System;
using Gma.System.MouseKeyHook;

namespace FocusTracker.Core.Services
{
    // Відстежує останнє введення з миші/клавіатури
    public class UserActivityTracker
    {
        // Секунда вважається активною, якщо введення було не пізніше ніж стільки тому
        public static readonly TimeSpan ActivityWindow = TimeSpan.FromSeconds(5);

        private long _lastInputTicks = DateTime.Now.Ticks;
        private IKeyboardMouseEvents? _events;

        public DateTime LastInputTime => new(Interlocked.Read(ref _lastInputTicks));

        public void Start()
        {
            Interlocked.Exchange(ref _lastInputTicks, DateTime.Now.Ticks);

            _events = Hook.GlobalEvents();
            _events.MouseMove += OnUserActivity;
            _events.KeyDown += OnUserActivity;
        }

        public void Stop()
        {
            if (_events == null) return;

            _events.MouseMove -= OnUserActivity;
            _events.KeyDown -= OnUserActivity;
            _events.Dispose();
            _events = null;
        }

        // Активна частина інтервалу: весь інтервал, якщо користувач щось вводив нещодавно,
        // інакше нуль. Так активний час ніколи не перевищує загальний.
        public TimeSpan GetActivePart(TimeSpan interval) =>
            DateTime.Now - LastInputTime <= ActivityWindow ? interval : TimeSpan.Zero;

        private void OnUserActivity(object? sender, EventArgs e)
        {
            Interlocked.Exchange(ref _lastInputTicks, DateTime.Now.Ticks);
        }
    }
}
