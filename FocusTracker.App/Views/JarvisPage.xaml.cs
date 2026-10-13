using FocusTracker.App.Jarvis;
using FocusTracker.Data.Cloud;
using FocusTracker.Domain;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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

        // Розмова живе, поки працює застосунок: сторінка створюється заново при кожному відкритті
        private static readonly ObservableCollection<ChatItem> Chat = new();

        // Хмарний режим: спільна розмова з бази, завантажується щоразу заново
        private static readonly ObservableCollection<ChatItem> CloudChat = new();
        private static readonly HashSet<long> CloudShown = new();

        private readonly JarvisService _jarvis;
        private readonly CloudAssistant _cloud;
        private readonly FocusMode _focus;
        private readonly bool _cloudMode;
        private bool _sending;

        public JarvisPage()
        {
            InitializeComponent();
            _jarvis = App.Services.GetRequiredService<JarvisService>();
            _cloud = App.Services.GetRequiredService<CloudAssistant>();
            _focus = App.Services.GetRequiredService<FocusMode>();
            _cloudMode = _cloud.IsActive;

            if (_cloudMode)
            {
                ChatItems.ItemsSource = CloudChat;
                _ = LoadCloudHistoryAsync();
            }
            else
            {
                ChatItems.ItemsSource = Chat;
                if (Chat.Count == 0)
                {
                    foreach (var note in _jarvis.Notes)
                        Chat.Add(FromNote(note));
                }
            }

            _jarvis.NoteAdded += OnNoteAdded;
            _cloud.MessageReceived += OnCloudMessage;
            _focus.Changed += OnFocusChanged;
            Unloaded += (_, _) =>
            {
                _jarvis.NoteAdded -= OnNoteAdded;
                _cloud.MessageReceived -= OnCloudMessage;
                _focus.Changed -= OnFocusChanged;
            };

            UpdateStatus();
            Loaded += (_, _) => { ChatScroll.ScrollToEnd(); InputBox.Focus(); };
        }

        private static ChatItem FromNote(JarvisNote note) => new($"{AssistantInfo.Name} · {note.At:HH:mm}", note.Text, false);

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

        private void OnNoteAdded(JarvisNote note) => Dispatcher.Invoke(() =>
        {
            if (_cloudMode) return;
            Chat.Add(FromNote(note));
            ChatScroll.ScrollToEnd();
            UpdateStatus();
        });

        private void OnCloudMessage(CloudAssistantMessage message) => Dispatcher.Invoke(() =>
        {
            if (!_cloudMode) return;
            AddCloud(message);
            ChatScroll.ScrollToEnd();
            UpdateStatus();
        });

        private static void AddCloud(CloudAssistantMessage message)
        {
            if (CloudShown.Add(message.Id))
                CloudChat.Add(FromCloud(message));
        }

        private async Task LoadCloudHistoryAsync()
        {
            try
            {
                var messages = await _cloud.LoadHistoryAsync();
                CloudChat.Clear();
                CloudShown.Clear();
                foreach (var message in messages)
                    AddCloud(message);
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
            if (_cloudMode)
            {
                parts.Add(_sending ? "Думаю…" : $"{AssistantInfo.Name} працює в хмарі (Supabase).");
                if (_cloud.State?.LastRunAtUtc is { } run)
                    parts.Add($"Останній запуск: {run.ToLocalTime():HH:mm}.");
                if (_cloud.State?.LastError is { } cloudError)
                    parts.Add($"Помилка в хмарі: {cloudError}");
                if (_cloud.LastError is { } connectionError)
                    parts.Add($"Немає зв'язку з хмарою: {connectionError}");
                if (_focus.IsActive)
                    parts.Add($"Фокус-режим до {_focus.Until:HH:mm}: заблоковано {string.Join(", ", _focus.BlockedApps)}.");

                StatusText.Text = string.Join(" ", parts);
                StopFocusButton.Visibility = _focus.IsActive ? Visibility.Visible : Visibility.Collapsed;
                CheckNowButton.IsEnabled = !_sending;
                SendButton.IsEnabled = !_sending;
                return;
            }

            if (!_jarvis.IsConfigured)
                parts.Add($"{AssistantInfo.Name} вимкнений. Увімкніть асистента й вставте ключ Anthropic API у «Налаштування».");
            else if (_jarvis.LastCheckAt is { } at)
                parts.Add($"Остання перевірка: {at:HH:mm}.");
            if (_jarvis.LastError is { } error)
                parts.Add($"Помилка: {error}");
            if (_focus.IsActive)
                parts.Add($"Фокус-режим до {_focus.Until:HH:mm}: заблоковано {string.Join(", ", _focus.BlockedApps)}.");

            StatusText.Text = string.Join(" ", parts);
            StopFocusButton.Visibility = _focus.IsActive ? Visibility.Visible : Visibility.Collapsed;
            CheckNowButton.IsEnabled = _jarvis.IsConfigured;
            SendButton.IsEnabled = _jarvis.IsConfigured;
        }

        private async void CheckNow_Click(object sender, RoutedEventArgs e)
        {
            // У хмарі перевірка на вимогу — просто питання в чаті
            if (_cloudMode)
            {
                await SendToCloudAsync("Що мені зараз робити?");
                return;
            }

            CheckNowButton.IsEnabled = false;
            StatusText.Text = "Думаю…";
            try
            {
                var decision = await _jarvis.CheckAsync(JarvisCheckKind.Check);
                // Якщо втручатись не потрібно, кажемо про це прямо
                if (decision is { Action: "none" })
                    Chat.Add(new ChatItem($"{AssistantInfo.Name} · {DateTime.Now:HH:mm}", "Усе добре, продовжуй у тому ж дусі.", false));
            }
            finally
            {
                UpdateStatus();
                ChatScroll.ScrollToEnd();
            }
        }

        private async void StopFocus_Click(object sender, RoutedEventArgs e)
        {
            if (!_cloudMode)
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
            if (text.Length == 0) return;
            if (_cloudMode)
            {
                InputBox.Clear();
                await SendToCloudAsync(text);
                return;
            }
            if (!_jarvis.IsConfigured) return;

            InputBox.Clear();
            Chat.Add(new ChatItem($"Ви · {DateTime.Now:HH:mm}", text, true));
            ChatScroll.ScrollToEnd();
            SendButton.IsEnabled = false;

            try
            {
                // Розмова для Claude має починатися з користувача
                var history = Chat.SkipWhile(c => !c.FromUser).Select(c => (c.FromUser, c.Text)).ToList();
                var reply = await _jarvis.ChatAsync(history);
                Chat.Add(new ChatItem($"{AssistantInfo.Name} · {DateTime.Now:HH:mm}", reply ?? $"Не вдалося отримати відповідь. {_jarvis.LastError}", false));
            }
            finally
            {
                SendButton.IsEnabled = true;
                UpdateStatus();
                ChatScroll.ScrollToEnd();
            }
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
                CloudShown.Add(id);
                CloudChat.Add(new ChatItem($"Ви · {DateTime.Now:HH:mm}", text, true));
                ChatScroll.ScrollToEnd();

                var result = await _cloud.WaitForReplyAsync(id);
                if (result == null)
                    AddCloudError("Відповіді поки немає. Перевірте, що функцію розгорнуто, а хмару налаштовано («Налаштування» → «Налаштувати хмару»).");
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
            CloudChat.Add(new ChatItem($"{AssistantInfo.Name} · {DateTime.Now:HH:mm}", text, false));
    }
}
