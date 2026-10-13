using FocusTracker.App.Jarvis;
using FocusTracker.Data.Cloud;
using FocusTracker.Domain;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FocusTracker.App.Views
{
    public partial class JarvisPage : UserControl
    {
        public record ChatItem(string Header, string Text, bool FromUser)
        {
            public HorizontalAlignment Alignment => FromUser ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            public Brush Background => FromUser
                ? (Brush)Application.Current.FindResource("SecondAccentBrush")
                : (Brush)Application.Current.FindResource("AccentBrush");
        }

        // Спільна розмова з хмарної бази; завантажується щоразу заново при відкритті сторінки
        private static readonly ObservableCollection<ChatItem> Chat = new();
        private static readonly HashSet<long> Shown = new();

        // Підказка, коли хмарну базу ще не підключено
        public static readonly string NeedsCloudHint =
            $"{AssistantInfo.Name} працює в хмарі. Спершу підключіть хмарну базу в «Налаштування» → «Синхронізація між пристроями».";

        private readonly CloudAssistant _cloud;
        private readonly FocusMode _focus;
        private bool _sending;

        public JarvisPage()
        {
            InitializeComponent();
            _cloud = App.Services.GetRequiredService<CloudAssistant>();
            _focus = App.Services.GetRequiredService<FocusMode>();

            ChatItems.ItemsSource = Chat;
            if (_cloud.IsCloudConfigured)
                _ = LoadHistoryAsync();

            _cloud.MessageReceived += OnCloudMessage;
            _focus.Changed += OnFocusChanged;
            Unloaded += (_, _) =>
            {
                _cloud.MessageReceived -= OnCloudMessage;
                _focus.Changed -= OnFocusChanged;
            };

            UpdateStatus();
            Loaded += (_, _) => { ChatScroll.ScrollToEnd(); InputBox.Focus(); };
        }

        private static ChatItem FromCloud(CloudAssistantMessage message)
        {
            var at = message.CreatedAtUtc.ToLocalTime();
            var time = at.Date == DateTime.Today ? at.ToString("HH:mm") : at.ToString("dd.MM HH:mm");
            if (message.Role == "user")
                return new ChatItem($"Ви · {time}", message.Text, true);

            var header = message.Kind switch
            {
                "focus" => $"{AssistantInfo.Name} · {time} · фокус-режим",
                "plan" => $"{AssistantInfo.Name} · {time} · план",
                "review" => $"{AssistantInfo.Name} · {time} · підсумок дня",
                _ => $"{AssistantInfo.Name} · {time}"
            };
            return new ChatItem(header, message.Text, false);
        }

        private void OnCloudMessage(CloudAssistantMessage message) => Dispatcher.Invoke(() =>
        {
            Add(message);
            ChatScroll.ScrollToEnd();
            UpdateStatus();
        });

        private static void Add(CloudAssistantMessage message)
        {
            if (Shown.Add(message.Id))
                Chat.Add(FromCloud(message));
        }

        private async Task LoadHistoryAsync()
        {
            try
            {
                var messages = await _cloud.LoadHistoryAsync();
                Chat.Clear();
                Shown.Clear();
                foreach (var message in messages)
                    Add(message);
                ChatScroll.ScrollToEnd();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Не вдалося завантажити розмову з хмари: {ex.Message}";
            }
        }

        private void OnFocusChanged() => Dispatcher.Invoke(UpdateStatus);

        private void UpdateStatus()
        {
            StopFocusButton.Visibility = _focus.IsActive ? Visibility.Visible : Visibility.Collapsed;
            if (!_cloud.IsCloudConfigured)
            {
                StatusText.Text = NeedsCloudHint;
                CheckNowButton.IsEnabled = false;
                SendButton.IsEnabled = false;
                return;
            }

            var parts = new List<string>();
            if (_sending)
                parts.Add("Думаю…");
            else if (!_cloud.IsActive)
                parts.Add($"{AssistantInfo.Name} вимкнений. Увімкніть його в «Налаштування» і збережіть.");
            else
                parts.Add($"{AssistantInfo.Name} працює в хмарі (Supabase).");
            if (_cloud.State?.LastRunAtUtc is { } run)
                parts.Add($"Останній запуск: {run.ToLocalTime():HH:mm}.");
            if (_cloud.State?.LastError is { } cloudError)
                parts.Add($"Помилка в хмарі: {cloudError}");
            if (_cloud.SetupError is { } setupError)
                parts.Add($"Не вдалося налаштувати базу: {setupError}");
            if (_cloud.LastError is { } connectionError)
                parts.Add($"Немає зв'язку з хмарою: {connectionError}");
            if (_focus.IsActive)
                parts.Add($"Фокус-режим до {_focus.Until:HH:mm}: заблоковано {string.Join(", ", _focus.BlockedApps)}.");

            StatusText.Text = string.Join(" ", parts);
            CheckNowButton.IsEnabled = _cloud.IsActive && !_sending;
            SendButton.IsEnabled = _cloud.IsActive && !_sending;
        }

        // Перевірка на вимогу — просто питання в чаті
        private async void CheckNow_Click(object sender, RoutedEventArgs e) =>
            await SendToCloudAsync("Що мені зараз робити?");

        private async void StopFocus_Click(object sender, RoutedEventArgs e)
        {
            if (!_cloud.IsCloudConfigured)
            {
                _focus.Stop();
                return;
            }

            // Фокус-режим спільний для всіх пристроїв: знімаємо його і в хмарі
            StopFocusButton.IsEnabled = false;
            try
            {
                await _cloud.StopFocusAsync();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Фокус-режим тут вимкнено, але не в хмарі: {ex.Message}";
            }
            StopFocusButton.IsEnabled = true;
        }

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                Send_Click(sender, e);
        }

        private async void Send_Click(object sender, RoutedEventArgs e)
        {
            var text = InputBox.Text.Trim();
            if (text.Length == 0 || !_cloud.IsActive || _sending) return;
            InputBox.Clear();
            await SendToCloudAsync(text);
        }

        // Повідомлення йде в базу, тригер будить хмарну функцію, а ми чекаємо на її відповідь
        private async Task SendToCloudAsync(string text)
        {
            _sending = true;
            SendButton.IsEnabled = false;
            CheckNowButton.IsEnabled = false;
            StatusText.Text = "Думаю…";
            try
            {
                var id = await _cloud.SendAsync(text);
                Shown.Add(id);
                Chat.Add(new ChatItem($"Ви · {DateTime.Now:HH:mm}", text, true));
                ChatScroll.ScrollToEnd();

                var result = await _cloud.WaitForReplyAsync(id);
                if (result == null)
                    AddCloudError("Відповіді поки немає. Перевірте, що функцію assistant розгорнуто, а в Supabase задано ANTHROPIC_API_KEY.");
                else if (result.Status == "error")
                    AddCloudError($"Не вдалося отримати відповідь. {result.Error}");

                // Відповідь прийде через MessageReceived
                await _cloud.PollNowAsync();
            }
            catch (Exception ex)
            {
                AddCloudError($"Не вдалося надіслати: {ex.Message}");
            }
            finally
            {
                _sending = false;
                UpdateStatus();
                ChatScroll.ScrollToEnd();
            }
        }

        private void AddCloudError(string text) =>
            Chat.Add(new ChatItem($"{AssistantInfo.Name} · {DateTime.Now:HH:mm}", text, false));
    }
}
