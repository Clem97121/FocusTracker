using FocusTracker.Domain.Models;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace FocusTracker.App.Converters
{
    public class ProgramSelectedConverter : IMultiValueConverter
    {
        // Convert: возвращает true, если текущая программа выбрана (в списке SelectedPrograms)
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length != 2)
                return false;

            if (values[0] is ObservableCollection<TrackedProgram> selectedPrograms &&
                values[1] is TrackedProgram program)
            {
                return selectedPrograms.Contains(program);
            }

            return false;
        }

        // ConvertBack: добавляет или удаляет программу из SelectedPrograms
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            // Возвращаем пустой массив — добавление/удаление будет обработано в коде-behind или ViewModel вручную.
            return new object[2];
        }
    }
}
