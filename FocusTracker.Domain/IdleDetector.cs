namespace FocusTracker.Domain
{
    // Вирішує, чи користувач присутній за пристроєм. Ознаки присутності — введення,
    // звук або повноекранне вікно у програми у фокусі. Після останньої ознаки час
    // рахується ще Threshold, далі пристрій вважається таким, що простоює.
    public class IdleDetector
    {
        public static readonly TimeSpan DefaultThreshold = TimeSpan.FromMinutes(3);

        private DateTime _lastSignal;

        public IdleDetector(DateTime start, TimeSpan? threshold = null)
        {
            _lastSignal = start;
            Threshold = threshold ?? DefaultThreshold;
        }

        public TimeSpan Threshold { get; set; }

        public DateTime LastSignal => _lastSignal;

        public void Signal(DateTime at)
        {
            if (at > _lastSignal)
                _lastSignal = at;
        }

        // ignoresIdle — програма позначена «не враховувати простій»
        public bool IsPresent(DateTime now, bool ignoresIdle = false) =>
            ignoresIdle || now - _lastSignal <= Threshold;
    }
}
