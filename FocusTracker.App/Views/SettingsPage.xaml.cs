using FocusTracker.App.Helpers;
using FocusTracker.App.Services;
using FocusTracker.Data;
using FocusTracker.App.Jarvis;
using FocusTracker.Data.Cloud;
using FocusTracker.Domain;
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

            LoadJarvisSettings();
            LoadOwner();

            _sync = App.Services.GetRequiredService<CloudSyncRunner>();
            LoadSyncSettings();
            _sync.StatusChanged += OnSyncStatusChanged;
            Unloaded += (_, _) => _sync.StatusChanged -= OnSyncStatusChanged;
        }

        private readonly CloudSyncRunner _sync;

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
                _settings.Update(s => s.IdleThresholdMinutes = (int)IdleSlider.Value);
                SavedText.Text = "Збережено";
            }
            catch (Exception ex)
            {
                AppLog.Error("Не вдалося зберегти налаштування", ex);
                SavedText.Text = "Не вдалося зберегти";
            }
            SavedText.Visibility = Visibility.Visible;
        }

        private void LoadSyncSettings()
        {
            var current = _settings.Current;
            DeviceNameBox.Text = current.DeviceName;
            SyncEnabledBox.IsChecked = current.CloudSyncEnabled;
            ConnectionHint.Text = CloudCredentials.Unprotect(current.CloudConnectionProtected) != null
                ? "Рядок підключення збережено. Залиште поле порожнім, щоб не змінювати його."
                : "Рядок підключення ще не задано.";
            ShowSyncStatus();
        }

        private void ShowSyncStatus()
        {
            if (!_sync.IsConfigured)
            {
                SyncStatusText.Text = "Синхронізацію вимкнено.";
                return;
            }

            if (_sync.LastError != null)
                SyncStatusText.Text = $"Помилка синхронізації: {_sync.LastError}";
            else if (_sync.LastSuccessAt is { } at && _sync.LastResult is { } result)
                SyncStatusText.Text = $"Остання синхронізація: {at:HH:mm:ss}. Відправлено рядків: {result.Uploaded}, отримано: {result.Downloaded}.";
            else
                SyncStatusText.Text = "Очікую першої синхронізації…";
        }

        // Введений рядок підключення, а якщо поле порожнє — збережений
        private string? GetConnectionString() =>
            string.IsNullOrWhiteSpace(ConnectionBox.Password)
                ? CloudCredentials.Unprotect(_settings.Current.CloudConnectionProtected)
                : ConnectionBox.Password.Trim();

        private async void SaveSync_Click(object sender, RoutedEventArgs e)
        {
            SaveSyncButton.IsEnabled = false;
            try
            {
                var newConnection = ConnectionBox.Password.Trim();
                var oldConnection = CloudCredentials.Unprotect(_settings.Current.CloudConnectionProtected);

                // Інша база — інші дані: відправимо все заново й заберемо чужі рядки з нової бази
                if (newConnection.Length > 0 && newConnection != oldConnection)
                    await App.Services.GetRequiredService<CloudSyncService>().ResetAsync();

                _settings.Update(s =>
                {
                    s.DeviceName = DeviceNameBox.Text;
                    s.CloudSyncEnabled = SyncEnabledBox.IsChecked == true;
                    if (newConnection.Length > 0)
                        s.CloudConnectionProtected = CloudCredentials.Protect(newConnection);
                });

                ConnectionBox.Clear();
                LoadSyncSettings();

                if (_sync.IsConfigured)
                {
                    SyncStatusText.Text = "Синхронізую…";
                    await _sync.SyncNowAsync();
                }
                ShowSyncStatus();
            }
            catch (Exception ex)
            {
                AppLog.Error("Не вдалося зберегти налаштування синхронізації", ex);
                SyncStatusText.Text = $"Не вдалося зберегти: {ex.Message}";
            }
            SaveSyncButton.IsEnabled = true;
        }

        private async void TestConnection_Click(object sender, RoutedEventArgs e)
        {
            var connection = GetConnectionString();
            if (connection == null)
            {
                SyncStatusText.Text = "Спершу вставте рядок підключення.";
                return;
            }

            TestConnectionButton.IsEnabled = false;
            SyncStatusText.Text = "Перевіряю підключення…";
            try
            {
                await CloudSyncService.TestConnectionAsync(new CloudStore(connection));
                SyncStatusText.Text = "Підключення працює.";
            }
            catch (Exception ex)
            {
                SyncStatusText.Text = $"Не вдалося підключитися: {ex.Message}";
            }
            TestConnectionButton.IsEnabled = true;
        }

        private void OnSyncStatusChanged() => Dispatcher.Invoke(ShowSyncStatus);

        private async void LoadOwner()
        {
            var connection = CloudCredentials.Unprotect(_settings.Current.CloudConnectionProtected);
            if (connection == null)
            {
                OwnerStatusText.Text = "Спершу налаштуйте синхронізацію вище.";
                BindOwnerButton.IsEnabled = false;
                return;
            }

            try
            {
                var email = await new CloudStore(connection).GetOwnerEmailAsync();
                OwnerStatusText.Text = email == null ? "Телефон ще не прив'язано." : $"Власник: {email}";
                if (email != null) OwnerEmailBox.Text = email;
            }
            catch (Exception ex)
            {
                OwnerStatusText.Text = $"Не вдалося перевірити: {ex.Message}";
            }
        }

        private async void BindOwner_Click(object sender, RoutedEventArgs e)
        {
            var connection = CloudCredentials.Unprotect(_settings.Current.CloudConnectionProtected);
            var email = OwnerEmailBox.Text.Trim();
            if (connection == null || email.Length == 0)
            {
                OwnerStatusText.Text = "Введіть email облікового запису Supabase.";
                return;
            }

            BindOwnerButton.IsEnabled = false;
            OwnerStatusText.Text = "Прив'язую…";
            try
            {
                OwnerStatusText.Text = await new CloudStore(connection).BindOwnerAsync(email)
                    ? $"Власник: {email}. Цим email і паролем входьте в застосунку на телефоні."
                    : "Користувача з таким email у Supabase немає. Створіть його: Authentication → Users → Add user.";
            }
            catch (Exception ex)
            {
                AppLog.Error("Не вдалося прив'язати власника для телефону", ex);
                OwnerStatusText.Text = $"Не вдалося прив'язати: {ex.Message}";
            }
            BindOwnerButton.IsEnabled = true;
        }

        private void LoadJarvisSettings()
        {
            var s = _settings.Current;
            AssistantHeader.Text = AssistantInfo.Name;
            JarvisEnabledBox.IsChecked = s.JarvisEnabled;
            JarvisModelBox.SelectedValue = s.JarvisModel;
            if (JarvisModelBox.SelectedIndex < 0) JarvisModelBox.SelectedIndex = 0;
            GoalsBox.Text = s.Goals;
            RulesBox.Text = s.JarvisRules;
            FromHourBox.Text = s.ProductiveFromHour.ToString();
            ToHourBox.Text = s.ProductiveToHour.ToString();
            CheckMinutesBox.Text = s.JarvisCheckMinutes.ToString();
            MayFocusBox.IsChecked = s.JarvisMayStartFocus;
        }

        private async void SaveJarvis_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(FromHourBox.Text, out var from) || !int.TryParse(ToHourBox.Text, out var to) || from >= to
                || !int.TryParse(CheckMinutesBox.Text, out var minutes))
            {
                JarvisStatusText.Text = "Перевірте години (з < до) і інтервал.";
                return;
            }

            try
            {
                _settings.Update(s =>
                {
                    s.JarvisEnabled = JarvisEnabledBox.IsChecked == true;
                    s.JarvisModel = JarvisModelBox.SelectedValue as string ?? AppSettings.DefaultJarvisModel;
                    s.Goals = GoalsBox.Text.Trim();
                    s.JarvisRules = RulesBox.Text.Trim();
                    s.ProductiveFromHour = from;
                    s.ProductiveToHour = to;
                    s.JarvisCheckMinutes = minutes;
                    s.JarvisMayStartFocus = MayFocusBox.IsChecked == true;
                });
                LoadJarvisSettings();
                JarvisStatusText.Text = "Збережено";
            }
            catch (Exception ex)
            {
                AppLog.Error("Не вдалося зберегти налаштування асистента", ex);
                JarvisStatusText.Text = "Не вдалося зберегти";
                return;
            }

            // Асистент працює в хмарі: туди йдуть налаштування (і вимкнення теж), а заодно
            // перевіряється розклад і тригер чату в базі
            if (!_sync.IsConfigured)
            {
                if (_settings.Current.JarvisEnabled)
                    JarvisStatusText.Text = "Збережено. Щоб асистент запрацював, налаштуйте синхронізацію з Supabase вище.";
                return;
            }
            try
            {
                JarvisStatusText.Text = "Відправляю в хмару…";
                await App.Services.GetRequiredService<CloudAssistant>().UploadSettingsAsync();
                JarvisStatusText.Text = "Збережено";
            }
            catch (Exception ex)
            {
                AppLog.Error("Не вдалося відправити налаштування асистента в хмару", ex);
                JarvisStatusText.Text = $"Збережено, але не відправлено в хмару: {ex.Message}";
            }
        }
    }
}
