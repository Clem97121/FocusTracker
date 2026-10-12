using FocusTracker.App.Helpers;
using FocusTracker.App.Services;
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

            _updates = App.Services.GetRequiredService<UpdateService>();
            VersionText.Text = $"Поточна версія: {_updates.CurrentVersion}";
            if (!_updates.IsInstalled)
            {
                CheckUpdatesButton.IsEnabled = false;
                UpdateStatusText.Text = "Оновлення доступні лише у версії, встановленій через Setup.exe.";
            }
            else if (_updates.AvailableVersion != null)
            {
                ShowAvailable(_updates.AvailableVersion);
            }
        }

        private readonly UpdateService _updates;

        private void ShowAvailable(string version)
        {
            UpdateStatusText.Text = $"Доступна нова версія {version}.";
            InstallUpdateButton.Visibility = Visibility.Visible;
        }

        private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            CheckUpdatesButton.IsEnabled = false;
            UpdateStatusText.Text = "Перевіряю…";
            try
            {
                var version = await _updates.CheckAsync();
                if (version != null)
                    ShowAvailable(version);
                else
                    UpdateStatusText.Text = "У вас остання версія.";
            }
            catch (Exception ex)
            {
                AppLog.Error("Не вдалося перевірити оновлення", ex);
                UpdateStatusText.Text = $"Не вдалося перевірити оновлення: {ex.Message}";
            }
            CheckUpdatesButton.IsEnabled = true;
        }

        private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
        {
            InstallUpdateButton.IsEnabled = false;
            CheckUpdatesButton.IsEnabled = false;
            try
            {
                await ((App)Application.Current).InstallUpdateAsync(
                    percent => Dispatcher.Invoke(() => UpdateStatusText.Text = $"Завантаження оновлення… {percent}%"));
            }
            catch (Exception ex)
            {
                AppLog.Error("Не вдалося встановити оновлення", ex);
                UpdateStatusText.Text = $"Не вдалося оновити: {ex.Message}";
                InstallUpdateButton.IsEnabled = true;
                CheckUpdatesButton.IsEnabled = true;
            }
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
