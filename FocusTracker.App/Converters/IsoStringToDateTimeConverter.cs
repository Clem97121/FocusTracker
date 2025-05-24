using System;
using System.Globalization;
using System.Windows.Data;

namespace FocusTracker.App.Converters
{
    public class IsoStringToDateTimeConverter : IValueConverter
    {
        // Преобразование из ISO-строки в отформатированную дату
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string s && DateTime.TryParse(s, out var dt))
            {
                // здесь удобный формат: день.месяц.год час:минута
                return dt.ToString("dd.MM.yyyy HH:mm");
            }
            return value; // если не получилось, вернём как есть
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
