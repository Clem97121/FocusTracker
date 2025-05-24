namespace FocusTracker.App.Helpers
{
    public static class TimeSpanExtensions
    {
        public static string ToReadableString(this TimeSpan timeSpan)
        {
            if (timeSpan.TotalHours >= 1)
                return $"{(int)timeSpan.TotalHours} год {(int)timeSpan.Minutes} хв";
            else if (timeSpan.TotalMinutes >= 1)
                return $"{(int)timeSpan.TotalMinutes} хв {(int)timeSpan.Seconds} с";
            else
                return $"{(int)timeSpan.TotalSeconds} с";
        }
    }
}
