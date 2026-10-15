using System.Collections.ObjectModel;
using FocusTracker.Mobile.Services;

namespace FocusTracker.Mobile.Pages;

// Спільний чат із Коучем: ті самі повідомлення, що на сторінці «Коуч» на ПК
public class ChatPage : ContentPage
{
    public record ChatItem(string Header, string Text, bool FromUser)
    {
        public LayoutOptions Alignment => FromUser ? LayoutOptions.End : LayoutOptions.Start;
        public Color Background => FromUser ? Color.FromArgb("#4A3F8C") : Color.FromArgb("#2B2B33");
    }

    private readonly CoachClient _client = new();
    private readonly ObservableCollection<ChatItem> _items = new();
    private readonly HashSet<long> _shown = new();
    private readonly CollectionView _list;
    private readonly Entry _input = new() { Placeholder = "Напишіть Коучу…", ReturnType = ReturnType.Send };
    private readonly Button _send = new() { Text = "➤", WidthRequest = 56 };
    private readonly Button _whatNow = new() { Text = "Що мені зараз робити?" };
    private readonly Label _status = new() { TextColor = Colors.Gray, FontSize = 13 };
    private long _lastId;
    private bool _sending;
    private IDispatcherTimer? _timer;

    public ChatPage()
    {
        Title = "Коуч";
        _list = new CollectionView
        {
            ItemsSource = _items,
            ItemTemplate = new DataTemplate(() =>
            {
                var header = new Label { FontSize = 11, TextColor = Colors.Gray };
                header.SetBinding(Label.TextProperty, nameof(ChatItem.Header));
                var text = new Label { FontSize = 15, TextColor = Colors.White };
                text.SetBinding(Label.TextProperty, nameof(ChatItem.Text));
                var bubble = new Border
                {
                    Padding = new Thickness(12, 8),
                    Margin = new Thickness(0, 4),
                    StrokeThickness = 0,
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
                    MaximumWidthRequest = 320,
                    Content = new VerticalStackLayout { Spacing = 2, Children = { header, text } },
                };
                bubble.SetBinding(View.HorizontalOptionsProperty, nameof(ChatItem.Alignment));
                bubble.SetBinding(BackgroundColorProperty, nameof(ChatItem.Background));
                return bubble;
            }),
        };

        _send.Clicked += async (_, _) => await SendAsync(_input.Text);
        _input.Completed += async (_, _) => await SendAsync(_input.Text);
        _whatNow.Clicked += async (_, _) => await SendAsync("Що мені зараз робити?");

        var grid = new Grid
        {
            Padding = 12,
            RowSpacing = 8,
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) },
        };
        grid.Add(new VerticalStackLayout { Spacing = 6, Children = { _whatNow, _status } }, 0, 0);
        grid.Add(_list, 0, 1);
        grid.Add(new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            Children = { _input, _send.Column(1) },
        }, 0, 2);
        Content = grid;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            foreach (var message in await _client.RecentAsync())
                Add(message);
            ScrollToEnd();
        }
        catch (Exception ex)
        {
            _status.Text = $"Не вдалося завантажити розмову: {ex.Message}";
        }

        // Поки чат відкрито, підтягуємо нові повідомлення (і з ПК теж)
        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(15);
        _timer.Tick += async (_, _) => await PollAsync();
        _timer.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _timer?.Stop();
    }

    private void Add(CoachMessage message)
    {
        _lastId = Math.Max(_lastId, message.Id);
        if (!_shown.Add(message.Id)) return;

        var at = message.CreatedAtUtc.ToLocalTime();
        var time = at.Date == DateTime.Today ? at.ToString("HH:mm") : at.ToString("dd.MM HH:mm");
        var header = message.Role == "user"
            ? (message.Kind == "event" ? $"Подія · {time}" : $"Ви · {time}")
            : message.Kind switch
            {
                "focus" => $"Коуч · {time} · фокус-режим",
                "plan" => $"Коуч · {time} · план",
                "review" => $"Коуч · {time} · підсумок дня",
                _ => $"Коуч · {time}",
            };
        _items.Add(new ChatItem(header, message.Text, message.Role == "user"));
    }

    private async Task PollAsync()
    {
        try
        {
            var messages = await _client.AfterAsync(_lastId);
            foreach (var message in messages)
                Add(message);
            if (messages.Count > 0) ScrollToEnd();
        }
        catch
        {
            // Без мережі спробуємо наступного разу
        }
    }

    private async Task SendAsync(string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text) || _sending) return;

        _sending = true;
        _send.IsEnabled = _whatNow.IsEnabled = false;
        _input.Text = "";
        _status.Text = "Думаю…";
        try
        {
            var id = await _client.SendAsync(text);
            await PollAsync();
            var processed = await _client.WaitProcessedAsync(id, TimeSpan.FromSeconds(90));
            _status.Text = processed == null
                ? "Відповіді поки немає, вона прийде сюди, щойно Коуч відповість."
                : processed.Status == "error" ? $"Не вдалося отримати відповідь: {processed.Error}" : "";
            await PollAsync();
        }
        catch (Exception ex)
        {
            _status.Text = $"Не вдалося надіслати: {ex.Message}";
        }
        finally
        {
            _sending = false;
            _send.IsEnabled = _whatNow.IsEnabled = true;
        }
    }

    private void ScrollToEnd()
    {
        if (_items.Count > 0)
            _list.ScrollTo(_items.Count - 1, position: ScrollToPosition.End, animate: false);
    }
}
