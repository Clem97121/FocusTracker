using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace FocusTracker.App.Converters
{
    public class CategoryToColorConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2) return Brushes.Transparent;

            if (values[0] is string category && values[1] is bool isHidden)
            {
                if (isHidden)
                    return Brushes.Gray;

                return category switch
                {
                    "Продуктивні" => new SolidColorBrush(Color.FromRgb(0x3C, 0xD1, 0x5F)),
                    "Непродуктивні" => new SolidColorBrush(Color.FromRgb(0xE8, 0x4D, 0x4D)),
                    "Суміжні" => new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x30)),
                    _ => Brushes.Transparent
                };
            }

            return Brushes.Transparent;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotImplementedException();
    }
}
