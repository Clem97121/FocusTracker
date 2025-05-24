using System;
using System.Globalization;
using System.Windows.Data;

namespace FocusTracker.App.Converters
{
    public class BoolToHistoryToggleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool b && b ? "Сховати історію" : "Показати історію";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
