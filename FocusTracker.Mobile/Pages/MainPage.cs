using System.Text.Json;
using FocusTracker.Mobile.Platforms.Android;
using FocusTracker.Mobile.Services;

namespace FocusTracker.Mobile.Pages;

// Стан синхронізації телефону й статистика за сьогодні з усіх пристроїв
public class MainPage : ContentPage
{
    private readonly PhoneAccount _account = new();
    private readonly Label _status = new() { TextColor = Colors.Gray };
    private readonly Button _permission = new() { Text = "Дати доступ до історії використання" };
    private readonly Button _sync = new() { Text = "Синхронізувати зараз" };
    private readonly Entry _deviceName = new() { Placeholder = "Назва пристрою" };
    private readonly Label _total = new() { FontSize = 18, FontAttributes = FontAttributes.Bold };
    private readonly VerticalStackLayout _apps = new() { Spacing = 6 };
    private readonly RefreshView _refresh = new();

    public MainPage()
    {
        Title = "FocusTracker";
        _deviceName.Text = _account.DeviceName;
        _deviceName.Unfocused += (_, _) => _account.DeviceName = _deviceName.Text;

        _permission.Clicked += (_, _) => UsageCollector.RequestPermission(Platform.CurrentActivity!);
        _sync.Clicked += async (_, _) => await SyncAndLoadAsync();
        _refresh.Command = new Command(async () => await SyncAndLoadAsync());

        ToolbarItems.Add(new ToolbarItem("Вийти", null, () =>
        {
            _account.SignOut();
            Application.Current!.Windows[0].Page = new NavigationPage(new LoginPage());
        }));

        _refresh.Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 20,
                Spacing = 12,
                Children =
                {
                    _status, _permission, _sync,
                    new Label { Text = "Назва цього телефону", TextColor = Colors.Gray }, _deviceName,
                    new Label { Text = "Сьогодні на всіх пристроях", FontSize = 20, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 12, 0, 0) },
                    _total, _apps,
                }
            }
        };
        Content = _refresh;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        SyncWorker.Schedule(Platform.AppContext);
        await SyncAndLoadAsync();
    }

    private async Task SyncAndLoadAsync()
    {
        _sync.IsEnabled = false;
        var hasPermission = UsageCollector.HasPermission(Platform.AppContext);
        _permission.IsVisible = !hasPermission;
        try
        {
            if (hasPermission)
            {
                _status.Text = "Синхронізую…";
                await new PhoneSync().SyncAsync();
            }
            await LoadTodayAsync();
        }
        catch (Exception ex)
        {
            _status.Text = $"Помилка: {ex.Message}";
            _refresh.IsRefreshing = false;
            _sync.IsEnabled = true;
            return;
        }

        _status.Text = hasPermission
            ? $"Синхронізовано о {PhoneSync.LastSyncAt:HH:mm}. Далі — автоматично кожні 15 хвилин."
            : "Щоб рахувати час на телефоні, дайте FocusTracker доступ до історії використання.";
        _refresh.IsRefreshing = false;
        _sync.IsEnabled = true;
    }

    private async Task LoadTodayAsync()
    {
        var api = _account.Api();
        var token = await _account.GetAccessTokenAsync();
        var today = DateTime.Today.ToString("yyyy-MM-dd");

        using var devices = await api.SelectAsync("ft_devices", "select=device_id,name", token);
        var names = devices.RootElement.EnumerateArray()
            .ToDictionary(d => d.GetProperty("device_id").GetString()!, d => d.GetProperty("name").GetString()!);

        using var rows = await api.SelectAsync("ft_hourly_usage", $"select=device_id,app_name,total_seconds&date=eq.{today}", token);
        var usage = rows.RootElement.EnumerateArray()
            .Select(r => (Device: r.GetProperty("device_id").GetString()!, App: r.GetProperty("app_name").GetString()!,
                Seconds: r.GetProperty("total_seconds").GetDouble()))
            .ToList();

        var total = TimeSpan.FromSeconds(usage.Sum(u => u.Seconds));
        var byDevice = usage.GroupBy(u => u.Device)
            .Select(g => $"{names.GetValueOrDefault(g.Key, "?")}: {Format(TimeSpan.FromSeconds(g.Sum(u => u.Seconds)))}");
        _total.Text = $"Разом {Format(total)}\n{string.Join(" · ", byDevice)}";

        _apps.Children.Clear();
        foreach (var app in usage.GroupBy(u => u.App).Select(g => (App: g.Key, Seconds: g.Sum(u => u.Seconds)))
                     .OrderByDescending(a => a.Seconds).Take(25))
        {
            _apps.Children.Add(new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
                Children =
                {
                    new Label { Text = app.App, LineBreakMode = LineBreakMode.TailTruncation },
                    new Label { Text = Format(TimeSpan.FromSeconds(app.Seconds)), TextColor = Colors.Gray }.Column(1),
                }
            });
        }
    }

    private static string Format(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours} год {t.Minutes} хв" : $"{t.Minutes} хв";
}

internal static class ViewExtensions
{
    public static T Column<T>(this T view, int column) where T : View
    {
        Grid.SetColumn(view, column);
        return view;
    }
}
