namespace FocusTracker.Domain
{
    public enum RestrictionAction
    {
        None,
        Warn,
        Wait,
        Close
    }

    // Відстрочка перед закриттям програми: спершу попередити, закрити лише якщо
    // порушення триває довше за Delay. Стан скидається, коли порушення зникло, і щодоби.
    public class RestrictionGracePeriod
    {
        public static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(60);

        private readonly Dictionary<string, DateTime> _warnedAt = new();

        public RestrictionGracePeriod(TimeSpan? delay = null)
        {
            Delay = delay ?? DefaultDelay;
        }

        public TimeSpan Delay { get; }

        public RestrictionAction Check(string appName, bool violated, DateTime now)
        {
            if (!violated)
            {
                _warnedAt.Remove(appName);
                return RestrictionAction.None;
            }

            if (!_warnedAt.TryGetValue(appName, out var warnedAt) || warnedAt.Date != now.Date)
            {
                _warnedAt[appName] = now;
                return RestrictionAction.Warn;
            }

            return now - warnedAt >= Delay ? RestrictionAction.Close : RestrictionAction.Wait;
        }
    }
}
