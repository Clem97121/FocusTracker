using FocusTracker.App.Helpers;
using FocusTracker.Data;
using FocusTracker.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Windows;
using System.Windows.Controls;

namespace FocusTracker.App.Views
{
    public partial class SettingsPage : UserControl
    {
        private readonly SettingsStore _settings;

        public SettingsPage()
        {
            InitializeComponent();

            _settings = App.Services.GetRequiredService<SettingsStore>();
            IdleSlider.Minimum = AppSettings.MinIdleThresholdMinutes;
            IdleSlider.Maximum = AppSettings.MaxIdleThresholdMinutes;
            IdleSlider.Value = _settings.Current.IdleThresholdMinutes;
            UpdateIdleText();
        }

        private void IdleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (IdleValueText == null) return;
            UpdateIdleText();
            SavedText.Visibility = Visibility.Collapsed;
        }

        private void UpdateIdleText() => IdleValueText.Text = $"{(int)IdleSlider.Value} хв";

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _settings.Save(new AppSettings { IdleThresholdMinutes = (int)IdleSlider.Value });
                SavedText.Text = "Збережено";
            }
            catch (Exception ex)
            {
                AppLog.Error("Не вдалося зберегти налаштування", ex);
                SavedText.Text = "Не вдалося зберегти";
            }
            SavedText.Visibility = Visibility.Visible;
        }
    }
}
