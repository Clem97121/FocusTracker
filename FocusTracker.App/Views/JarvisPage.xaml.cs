using FocusTracker.App.Jarvis;
using FocusTracker.Domain;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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

        private readonly JarvisService _jarvis;
        private readonly FocusMode _focus;

        public JarvisPage()
        {
            InitializeComponent();
            _jarvis = App.Services.GetRequiredService<JarvisService>();
            _focus = App.Services.GetRequiredService<FocusMode>();

            ChatItems.ItemsSource = Chat;
            if (Chat.Count == 0)
            {
                foreach (var note in _jarvis.Notes)
                    Chat.Add(FromNote(note));
            }

            _jarvis.NoteAdded += OnNoteAdded;
            _focus.Changed += OnFocusChanged;
            Unloaded += (_, _) =>
            {
                _jarvis.NoteAdded -= OnNoteAdded;
                _focus.Changed -= OnFocusChanged;
            };

            UpdateStatus();
            Loaded += (_, _) => { ChatScroll.ScrollToEnd(); InputBox.Focus(); };
        }

        private static ChatItem FromNote(JarvisNote note) => new($"Джарвіс · {note.At:HH:mm}", note.Text, false);

        private void OnNoteAdded(JarvisNote note) => Dispatcher.Invoke(() =>
        {
            Chat.Add(FromNote(note));
            ChatScroll.ScrollToEnd();
            UpdateStatus();
        });

        private void OnFocusChanged() => Dispatcher.Invoke(UpdateStatus);

        private void UpdateStatus()
        {
            var parts = new List<string>();
            if (!_jarvis.IsConfigured)
                parts.Add("Джарвіс вимкнений. Увімкніть його й вставте ключ Anthropic API у «Налаштування».");
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
            CheckNowButton.IsEnabled = false;
            StatusText.Text = "Думаю…";
            try
            {
                var decision = await _jarvis.CheckAsync(JarvisCheckKind.Check);
                // Якщо втручатись не потрібно, кажемо про це прямо
                if (decision is { Action: "none" })
                    Chat.Add(new ChatItem($"Джарвіс · {DateTime.Now:HH:mm}", "Усе добре, продовжуй у тому ж дусі.", false));
            }
            finally
            {
                UpdateStatus();
                ChatScroll.ScrollToEnd();
            }
        }

        private void StopFocus_Click(object sender, RoutedEventArgs e) => _focus.Stop();

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                Send_Click(sender, e);
        }

        private async void Send_Click(object sender, RoutedEventArgs e)
        {
            var text = InputBox.Text.Trim();
            if (text.Length == 0 || !_jarvis.IsConfigured) return;

            InputBox.Clear();
            Chat.Add(new ChatItem($"Ви · {DateTime.Now:HH:mm}", text, true));
            ChatScroll.ScrollToEnd();
            SendButton.IsEnabled = false;

            try
            {
                // Розмова для Claude має починатися з користувача
                var history = Chat.SkipWhile(c => !c.FromUser).Select(c => (c.FromUser, c.Text)).ToList();
                var reply = await _jarvis.ChatAsync(history);
                Chat.Add(new ChatItem($"Джарвіс · {DateTime.Now:HH:mm}", reply ?? $"Не вдалося отримати відповідь. {_jarvis.LastError}", false));
            }
            finally
            {
                SendButton.IsEnabled = true;
                UpdateStatus();
                ChatScroll.ScrollToEnd();
            }
        }
    }
}
