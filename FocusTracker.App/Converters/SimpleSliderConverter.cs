using System;
using System.Globalization;
using System.Windows.Data;

namespace FocusTracker.App.Converters
{
    public class SimpleSliderConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double val = System.Convert.ToDouble(value);
            double max = 10.0; // Slider.Maximum
            double width = 184; // 200 width - margins
            return (val / max) * width;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotImplementedException();
    }
}
