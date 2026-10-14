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
    private readonly Button _blocker = new() { Text = "Увімкнути блокування у фокус-режимі" };
    private readonly Label _focus = new() { TextColor = Color.FromArgb("#E0A040") };
    private readonly Entry _deviceName = new() { Placeholder = "Назва пристрою" };
    private readonly Label _total = new() { FontSize = 18, FontAttributes = FontAttributes.Bold };
    private readonly VerticalStackLayout _apps = new() { Spacing = 6 };
    private readonly RefreshView _refresh = new();
    private readonly Button _update = new() { IsVisible = false, BackgroundColor = Color.FromArgb("#3E8E5A"), TextColor = Colors.White };
    private AppUpdate? _available;

    public MainPage()
    {
        Title = "FocusTracker";
        _deviceName.Text = _account.DeviceName;
        _deviceName.Unfocused += (_, _) => _account.DeviceName = _deviceName.Text;

        _permission.Clicked += (_, _) => UsageCollector.RequestPermission(Platform.CurrentActivity!);
        _sync.Clicked += async (_, _) => await SyncAndLoadAsync();
        _refresh.Command = new Command(async () => await SyncAndLoadAsync());
        _update.Clicked += async (_, _) => await InstallUpdateAsync();
        _blocker.Clicked += async (_, _) =>
        {
            await DisplayAlertAsync("Блокування у фокус-режимі",
                "У списку «Встановлені програми» відкрийте «FocusTracker: фокус-режим» і ввімкніть. " +
                "Якщо Android пише «Обмежене налаштування»: Налаштування → Програми → FocusTracker → ⋮ → «Дозволити обмежені налаштування», і спробуйте ще раз.",
                "Відкрити налаштування");
            FocusBlockerService.OpenSettings(Platform.AppContext);
        };

        ToolbarItems.Add(new ToolbarItem("Коуч", null, async () => await Navigation.PushAsync(new ChatPage())));
        MainActivity.OpenChat += () => MainThread.BeginInvokeOnMainThread(OpenChatIfRequested);

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
                    _update, _status, _focus, _permission, _blocker, _sync,
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
        CoachNotifications.RequestPermission(Platform.CurrentActivity!);
        OpenChatIfRequested();
        _ = CheckUpdateAsync();
        await SyncAndLoadAsync();
    }

    private async Task CheckUpdateAsync()
    {
        try
        {
            _available = await AppUpdater.CheckAsync();
            if (_available == null) return;
            _update.Text = $"Доступна версія {_available.Version} — оновити";
            _update.IsVisible = true;
        }
        catch
        {
            // Без мережі перевіримо наступного разу
        }
    }

    // Завантажити APK з GitHub і відкрити системне встановлення поверх поточної версії
    private async Task InstallUpdateAsync()
    {
        if (_available == null) return;
        var context = Platform.AppContext;
        if (!ApkInstaller.CanInstall(context))
        {
            await DisplayAlertAsync("Оновлення", "Дозвольте FocusTracker встановлювати програми й натисніть «Оновити» ще раз.", "Добре");
            ApkInstaller.RequestInstallPermission(context);
            return;
        }

        _update.IsEnabled = false;
        try
        {
            var path = await AppUpdater.DownloadAsync(_available,
                new Progress<double>(p => _update.Text = $"Завантаження… {p:P0}"));
            await ApkInstaller.InstallAsync(path);
            _update.Text = $"Встановіть версію {_available.Version}";
        }
        catch (Exception ex)
        {
            _update.Text = $"Не вдалося завантажити: {ex.Message}";
        }
        finally
        {
            _update.IsEnabled = true;
        }
    }

    // Відкрили зі сповіщення Коуча — одразу в чат
    private async void OpenChatIfRequested()
    {
        if (!MainActivity.OpenChatRequested) return;
        MainActivity.OpenChatRequested = false;
        if (Navigation.NavigationStack.LastOrDefault() is not ChatPage)
            await Navigation.PushAsync(new ChatPage());
    }

    private async Task SyncAndLoadAsync()
    {
        _sync.IsEnabled = false;
        var hasPermission = UsageCollector.HasPermission(Platform.AppContext);
        _permission.IsVisible = !hasPermission;
        _blocker.IsVisible = !FocusBlockerService.IsEnabled(Platform.AppContext);
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

        _focus.Text = FocusState.IsActive
            ? $"Фокус-режим до {FocusState.UntilUtc!.Value.ToLocalTime():HH:mm}: {string.Join(", ", FocusState.Apps)}"
            : "";
        _focus.IsVisible = FocusState.IsActive;
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

        var top = usage.GroupBy(u => u.App).Select(g => (App: g.Key, Seconds: g.Sum(u => u.Seconds)))
            .OrderByDescending(a => a.Seconds).Take(25).ToList();
        var icons = await LoadIconsAsync(api, token, top.Select(a => a.App));

        _apps.Children.Clear();
        foreach (var app in top)
        {
            _apps.Children.Add(new Grid
            {
                ColumnSpacing = 10,
                ColumnDefinitions = { new ColumnDefinition(28), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
                Children =
                {
                    new Image
                    {
                        HeightRequest = 24, WidthRequest = 24,
                        Source = icons.TryGetValue(app.App, out var icon) ? ImageSource.FromStream(() => new MemoryStream(icon)) : null,
                    },
                    new Label { Text = app.App, LineBreakMode = LineBreakMode.TailTruncation, VerticalOptions = LayoutOptions.Center }.Column(1),
                    new Label { Text = Format(TimeSpan.FromSeconds(app.Seconds)), TextColor = Colors.Gray, VerticalOptions = LayoutOptions.Center }.Column(2),
                }
            });
        }
    }

    // Іконки зі спільної ft_program_icons: і програм телефону, і програм з ПК
    private static async Task<Dictionary<string, byte[]>> LoadIconsAsync(SupabaseApi api, string token, IEnumerable<string> apps)
    {
        var names = apps.Select(a => "\"" + a.Replace("\"", "\\\"") + "\"").ToList();
        if (names.Count == 0) return new();

        var filter = Uri.EscapeDataString($"({string.Join(",", names)})");
        using var rows = await api.SelectAsync("ft_program_icons", $"select=identifier,icon&identifier=in.{filter}", token);
        return rows.RootElement.EnumerateArray().ToDictionary(
            r => r.GetProperty("identifier").GetString()!,
            r => SupabaseApi.FromBytea(r.GetProperty("icon").GetString()!));
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
