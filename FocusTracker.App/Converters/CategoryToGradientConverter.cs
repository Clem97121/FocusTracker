using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using FocusTracker.Domain.Models;

namespace FocusTracker.App.Converters
{
    public class CategoryToGradientConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2) return Brushes.Transparent;

            if (values[0] is string category && values[1] is bool isHidden)
            {
                // Серый градиент для скрытых программ
                if (isHidden)
                {
                    Color gray = Color.FromRgb(120, 120, 120);
                    return new LinearGradientBrush(
                        new GradientStopCollection
                        {
                            new GradientStop(Color.FromArgb(0, gray.R, gray.G, gray.B), 0.0),
                            new GradientStop(Color.FromArgb(60, gray.R, gray.G, gray.B), 1.0)
                        },
                        new Point(0, 0),
                        new Point(1, 1)
                    );
                }

                // Цветной градиент по категории
                Color categoryColor = category switch
                {
                    "Продуктивні" => Color.FromRgb(0x3C, 0xD1, 0x5F),
                    "Непродуктивні" => Color.FromRgb(0xE8, 0x4D, 0x4D),
                    "Суміжні" => Color.FromRgb(0xFF, 0xB3, 0x30),
                    _ => Colors.Transparent
                };

                return new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(0, categoryColor.R, categoryColor.G, categoryColor.B), 0.0),
                        new GradientStop(Color.FromArgb(50, categoryColor.R, categoryColor.G, categoryColor.B), 1.0)
                    },
                    new Point(0, 0),
                    new Point(1, 1)
                );
            }

            return Brushes.Transparent;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
