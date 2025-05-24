using System;
using System.Globalization;
using System.Windows.Data;

namespace FocusTracker.App.Converters
{
    public class SliderValueToWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double sliderValue = System.Convert.ToDouble(value);
            double maxWidth = parameter != null ? System.Convert.ToDouble(parameter) : 200;
            double maxSlider = 10; // Максимальное значение слайдера
            return (sliderValue / maxSlider) * maxWidth;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
