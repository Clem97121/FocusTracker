using System;
using System.Globalization;
using System.Windows.Data;

namespace FocusTracker.App.Converters
{
    public class IsTextOverflowingConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double textWidth && double.TryParse(parameter?.ToString(), out double maxWidth))
            {
                return textWidth > maxWidth;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
