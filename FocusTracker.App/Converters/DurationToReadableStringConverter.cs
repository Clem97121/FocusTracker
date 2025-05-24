using System;
using System.Globalization;
using System.Windows.Data;

namespace FocusTracker.App.Converters
{
    public class DurationToReadableStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double minutes)
            {
                var totalSeconds = (int)(minutes * 60);
                if (totalSeconds < 60)
                {
                    return $"{totalSeconds} с";
                }
                else if (totalSeconds < 3600)
                {
                    var min = totalSeconds / 60;
                    var sec = totalSeconds % 60;
                    return sec > 0 ? $"{min} хв {sec} с" : $"{min} хв";
                }
                else
                {
                    var hours = totalSeconds / 3600;
                    var min = (totalSeconds % 3600) / 60;
                    return min > 0 ? $"{hours} год {min} хв" : $"{hours} год";
                }
            }

            return "0 с";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
