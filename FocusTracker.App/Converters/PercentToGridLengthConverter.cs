using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FocusTracker.App.Converters
{
    public class PercentToGridLengthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 2 &&
                values[0] is double percent &&
                values[1] is double actualWidth)
            {
                var actual = Math.Max(0, percent / 100.0 * actualWidth);
                return new GridLength(actual, GridUnitType.Pixel);
            }

            return new GridLength(0, GridUnitType.Pixel);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotImplementedException();
    }


}
