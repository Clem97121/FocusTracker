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

        // Спільна розмова з бази; завантажується заново при кожному відкритті сторінки
        private static readonly ObservableCollection<ChatItem> Chat = new();
        private static readonly HashSet<long> Shown = new();

        private readonly CloudAssistant _cloud;
        private readonly FocusMode _focus;
        private bool _sending;

        public JarvisPage()
        {
            InitializeComponent();
            _cloud = App.Services.GetRequiredService<CloudAssistant>();
            _focus = App.Services.GetRequiredService<FocusMode>();

            ChatItems.ItemsSource = Chat;
            if (_cloud.IsActive)
                _ = LoadHistoryAsync();

            _cloud.MessageReceived += OnMessage;
            _focus.Changed += OnFocusChanged;
            Unloaded += (_, _) =>
            {
                _cloud.MessageReceived -= OnMessage;
                _focus.Changed -= OnFocusChanged;
            };

            UpdateStatus();
            Loaded += (_, _) => { ChatScroll.ScrollToEnd(); InputBox.Focus(); };
        }

        private static ChatItem FromMessage(CloudAssistantMessage message)
        {
            var at = message.CreatedAtUtc.ToLocalTime();
            var time = at.Date == DateTime.Today ? at.ToString("HH:mm") : at.ToString("dd.MM HH:mm");
            if (message.Role == "user")
                return new ChatItem(message.Kind == "event" ? $"Подія · {time}" : $"Ви · {time}", message.Text, true);

            var header = message.Kind switch
            {
                "focus" => $"{AssistantInfo.Name} · {time} · фокус-режим",
                "plan" => $"{AssistantInfo.Name} · {time} · план",
                "review" => $"{AssistantInfo.Name} · {time} · підсумок дня",
                _ => $"{AssistantInfo.Name} · {time}"
            };
            return new ChatItem(header, message.Text, false);
        }

        private void OnMessage(CloudAssistantMessage message) => Dispatcher.Invoke(() =>
        {
            Add(message);
            ChatScroll.ScrollToEnd();
            UpdateStatus();
        });

        private static void Add(CloudAssistantMessage message)
        {
            if (Shown.Add(message.Id))
                Chat.Add(FromMessage(message));
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
            var parts = new List<string>();
            if (!_cloud.IsActive)
            {
                parts.Add($"{AssistantInfo.Name} вимкнений. Він працює в хмарі: налаштуйте синхронізацію з Supabase й увімкніть асистента в «Налаштування».");
            }
            else
            {
                parts.Add(_sending ? "Думаю…" : $"{AssistantInfo.Name} працює в хмарі (Supabase).");
                if (_cloud.State?.LastRunAtUtc is { } run)
                    parts.Add($"Останній запуск: {run.ToLocalTime():HH:mm}.");
                if (_cloud.State?.LastError is { } cloudError)
                    parts.Add($"Помилка в хмарі: {cloudError}");
                if (_cloud.LastError is { } connectionError)
                    parts.Add($"Немає зв'язку з хмарою: {connectionError}");
            }
            if (_focus.IsActive)
                parts.Add($"Фокус-режим до {_focus.Until:HH:mm}: заблоковано {string.Join(", ", _focus.BlockedApps)}.");

            StatusText.Text = string.Join(" ", parts);
            var focusVisibility = _focus.IsActive && _cloud.IsActive ? Visibility.Visible : Visibility.Collapsed;
            AskStopFocusButton.Visibility = focusVisibility;
            EmergencyStopButton.Visibility = focusVisibility;
            CheckNowButton.IsEnabled = _cloud.IsActive && !_sending;
            SendButton.IsEnabled = _cloud.IsActive && !_sending;
            MemoryButton.IsEnabled = _cloud.IsActive;
        }

        // Перевірка на вимогу — просто питання в чаті
        private async void CheckNow_Click(object sender, RoutedEventArgs e) => await SendAsync("Що мені зараз робити?");

        private void Memory_Click(object sender, RoutedEventArgs e)
        {
            if (Parent is ContentControl host)
                host.Content = new CoachMemoryPage();
        }

        // Фокус-режим знімає лише Коуч: пишемо йому причину
        private void AskStopFocus_Click(object sender, RoutedEventArgs e)
        {
            InputBox.Text = "Прошу зняти фокус-режим, бо ";
            InputBox.Focus();
            InputBox.CaretIndex = InputBox.Text.Length;
        }

        private async void EmergencyStop_Click(object sender, RoutedEventArgs e)
        {
            var answer = MessageBox.Show(
                $"Фокус-режим закінчиться через {CloudAssistant.EmergencyDelay.TotalMinutes:0} хвилин на всіх пристроях, і {AssistantInfo.Name} одразу про це дізнається. Продовжити?",
                "Екстрене зняття фокус-режиму", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;

            EmergencyStopButton.IsEnabled = false;
            try
            {
                var until = await _cloud.EmergencyStopAsync();
                StatusText.Text = until is { } u
                    ? $"Фокус-режим закінчиться о {u.ToLocalTime():HH:mm}."
                    : "Фокус-режим уже не активний.";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Не вдалося: {ex.Message}";
            }
            EmergencyStopButton.IsEnabled = true;
        }

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                Send_Click(sender, e);
        }

        private async void Send_Click(object sender, RoutedEventArgs e)
        {
            var text = InputBox.Text.Trim();
            if (text.Length == 0 || !_cloud.IsActive) return;
            InputBox.Clear();
            await SendAsync(text);
        }

        // Повідомлення йде в базу, тригер будить хмарну функцію, а ми чекаємо на її відповідь
        private async Task SendAsync(string text)
        {
            if (!_cloud.IsActive || _sending) return;

            _sending = true;
            UpdateStatus();
            try
            {
                var id = await _cloud.SendAsync(text);
                Shown.Add(id);
                Chat.Add(new ChatItem($"Ви · {DateTime.Now:HH:mm}", text, true));
                ChatScroll.ScrollToEnd();

                var result = await _cloud.WaitForReplyAsync(id);
                if (result == null)
                    AddError("Відповіді поки немає. Можливо, хмара ще налаштовується — спробуйте за хвилину.");
                else if (result.Status == "error")
                    AddError($"Не вдалося отримати відповідь. {result.Error}");

                // Відповідь прийде через MessageReceived
                await _cloud.PollNowAsync();
            }
            catch (Exception ex)
            {
                AddError($"Не вдалося надіслати: {ex.Message}");
            }
            finally
            {
                _sending = false;
                UpdateStatus();
                ChatScroll.ScrollToEnd();
            }
        }

        private static void AddError(string text) =>
            Chat.Add(new ChatItem($"{AssistantInfo.Name} · {DateTime.Now:HH:mm}", text, false));
    }
}
