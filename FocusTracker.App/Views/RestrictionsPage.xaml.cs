using FocusTracker.App.ViewModels;
using FocusTracker.Domain.Interfaces;
using FocusTracker.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FocusTracker.App.Views
{
    public partial class RestrictionsPage : UserControl
    {
        private RestrictionsPageViewModel ViewModel;

        public RestrictionsPage()
        {
            InitializeComponent();

            var restrictionService = App.Services.GetRequiredService<IRestrictionService>();
            var programService = App.Services.GetRequiredService<ITrackedProgramService>();

            var notificationService = App.Services.GetRequiredService<INotificationService>();
            ViewModel = new RestrictionsPageViewModel(restrictionService, programService, notificationService);

            DataContext = ViewModel;
        }

        private void ShowAddForm_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.IsEditing = false;
            AddForm.Visibility = Visibility.Visible;
        }

        private void CancelAddForm_Click(object sender, RoutedEventArgs e)
        {
            AddForm.Visibility = Visibility.Collapsed;
            ViewModel.ResetForm();
        }

        private async void AddRestriction_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.AddRestrictionAsync();
            AddForm.Visibility = Visibility.Collapsed;
        }

        private void ProgramsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is TrackedProgram selected)
            {
                if (!ViewModel.SelectedPrograms.Contains(selected))
                    ViewModel.SelectedPrograms.Add(selected);
            }

            ((ComboBox)sender).SelectedItem = null;
        }

        private void RemoveProgram_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is TrackedProgram program)
            {
                if (ViewModel.SelectedPrograms.Contains(program))
                    ViewModel.SelectedPrograms.Remove(program);
            }
        }

        private void EditRestriction_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is Restriction restriction)
            {
                ViewModel.StartEditing(restriction);
                AddForm.Visibility = Visibility.Visible;
            }
        }
        private void TimeInput_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Тільки цифри
            e.Handled = !e.Text.All(char.IsDigit);
        }

        private void HourTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox tb && int.TryParse(tb.Text, out int value))
            {
                if (value > 23) tb.Text = "23";
                if (value < 0) tb.Text = "00";
            }
        }

        private void MinuteTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox tb && int.TryParse(tb.Text, out int value))
            {
                if (value > 59) tb.Text = "59";
                if (value < 0) tb.Text = "00";
            }
        }

    }
}
