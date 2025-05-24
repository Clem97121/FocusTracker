using System;
using System.Globalization;
using System.Windows.Data;

namespace FocusTracker.App.Converters
{
    public class CardWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double totalWidth)
            {
                const double cardMinWidth = 300;
                const double spacing = 20;
                const double totalMargin = 40; // учёт внешнего отступа StackPanel

                double availableWidth = totalWidth - totalMargin;
                int cardsPerRow = Math.Max(1, (int)((availableWidth + spacing) / (cardMinWidth + spacing)));
                double totalSpacing = spacing * (cardsPerRow - 1);
                double usableWidth = availableWidth - totalSpacing;

                return usableWidth / cardsPerRow;
            }

            return 300;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
