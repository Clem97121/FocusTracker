using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using FocusTracker.App.Views;
using FocusTracker.Core.Services;
using FocusTracker.Data;
using FocusTracker.Data.Services;
using FocusTracker.Domain.Interfaces;
using FocusTracker.Domain.Models;
using FocusTracker.App.Services;
using System.IO;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Threading;
using System.IO.Pipes;

namespace FocusTracker.App
{
    public partial class App : Application
    {
        public static Action<string>? ShowUiNotification;
        public static IServiceProvider Services { get; private set; }

        private ActiveWindowTracker _windowTracker;
        private System.Windows.Forms.NotifyIcon _notifyIcon;
        private bool _exitRequested;
        public bool IsExitRequested => _exitRequested;

        private Timer _notificationTimer;
        private Timer _impulseTimer;

        private Mutex? _mutex;

        private readonly Dictionary<string, DateTime> _violationCache = new();

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private void StartPipeServer()
        {
            Task.Run(() =>
            {
                while (true)
                {
                    try
                    {
                        using var server = new NamedPipeServerStream("FocusTrackerPipe", PipeDirection.In);
                        server.WaitForConnection();

                        using var reader = new StreamReader(server);
                        var message = reader.ReadLine();

                        if (message == "SHOW")
                        {
                            Dispatcher.Invoke(() =>
                            {
                                var win = Current.MainWindow;

                                win.ShowInTaskbar = true;
                                win.Show();
                                win.WindowState = WindowState.Normal;

                                win.Topmost = true;           // принудительно наверх
                                win.Activate();               // активировать
                                win.Focus();                  // попытаться получить фокус
                                win.Topmost = false;          // вернуть как было
                            });
                        }

                    }
                    catch { /* игнор */ }
                }
            });
        }


        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        private void AddToStartup()
        {
            string appName = "FocusTracker";
            string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;

            using RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\\Microsoft\\Windows\\CurrentVersion\\Run", true);
            if (key.GetValue(appName) == null)
            {
                key.SetValue(appName, $"\"{exePath}\"");
            }
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            const string mutexName = "FocusTrackerAppSingleton";
            bool createdNew;
            _mutex = new Mutex(true, mutexName, out createdNew);

            if (!createdNew)
            {
                try
                {
                    using var client = new NamedPipeClientStream(".", "FocusTrackerPipe", PipeDirection.Out);
                    client.Connect(1000); // таймаут 1 сек
                    using var writer = new StreamWriter(client) { AutoFlush = true };
                    writer.WriteLine("SHOW");
                }
                catch { /* первая копия может ещё не готова */ }

                Shutdown();
                return;
            }


            base.OnStartup(e);

            AddToStartup();
            ConfigureServices();

            StartPipeServer();

            using (var scope = Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<FocusTrackerDbContext>();
                db.Database.Migrate();
            }

            ShowUiNotification = message => new CustomMessageBox(message).ShowDialog();

            using (var scope = Services.CreateScope())
            {
                var syncService = Services.GetRequiredService<ProgramSyncService>();
                await syncService.SyncAsync();
            }

            _notificationTimer = new Timer(async _ =>
            {
                using var scope = Services.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IRestrictionNotificationService>();
                await service.CheckAndNotifyAsync();
            }, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));

            _impulseTimer = new Timer(async _ =>
            {
                using var scope = Services.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IImpulseReminderService>();
                await service.CheckAndRemindAsync();
            }, null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));

            var userActivity = new UserActivityTracker();
            userActivity.Start();

            var programServiceForTracker = Services.GetRequiredService<IProgramService>();
            var nameResolver = Services.GetRequiredService<ProgramNameResolverService>();

            _windowTracker = new ActiveWindowTracker(
                async (appName, totalTime) =>
                {
                    using var scope = Services.CreateScope();
                    var statService = scope.ServiceProvider.GetRequiredService<IAppUsageStatService>();
                    var evaluator = scope.ServiceProvider.GetRequiredService<IRestrictionEvaluator>();
                    var notifier = scope.ServiceProvider.GetRequiredService<INotificationService>();

                    var activeTime = userActivity.GetAndResetActiveTime();
                    await statService.AddOrUpdateUsageAsync(appName, totalTime, activeTime);

                    var stats = await statService.GetStatsForTodayAsync();
                    var todayStat = stats.FirstOrDefault(s => s.AppName == appName);
                    var total = todayStat?.TotalTime ?? TimeSpan.Zero;

                    if (!evaluator.TryGetViolatedRestrictions(appName, total, out var notes)) return;

                    var cacheKey = $"{appName}_{string.Join(",", notes)}";
                    if (_violationCache.TryGetValue(cacheKey, out var lastShown) && (DateTime.Now - lastShown).TotalSeconds < 5)
                        return;

                    _violationCache[cacheKey] = DateTime.Now;
                    var message = $"Програму \"{appName}\" було закрито через перевищення обмеження:\n• {string.Join("\n• ", notes)}";
                    notifier.ShowMessage(message, "Обмеження перевищено");

                    var processes = Process.GetProcessesByName(appName);
                    foreach (var proc in processes)
                    {
                        try { proc.Kill(); } catch { }
                    }
                },
                programServiceForTracker,
                nameResolver
            );

            _windowTracker.Start();
            SetupTrayIcon();

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
        }

        private void ConfigureServices()
        {
            var services = new ServiceCollection();

            var dbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FocusTracker",
                "focus_tracker.db");
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath));

            services.AddDbContext<FocusTrackerDbContext>(options =>
                options.UseSqlite($"Data Source={dbPath}"));

            services.AddScoped<IAppUsageStatService, AppUsageStatService>();
            services.AddScoped<IRestrictionEvaluator, RestrictionEvaluator>();
            services.AddScoped<IProgramService, ProgramService>();
            services.AddScoped<ISkillService, SkillService>();
            services.AddScoped<ISkillCategoryService, SkillCategoryService>();
            services.AddScoped<ITaskItemService, TaskItemService>();
            services.AddScoped<ITaskProgramService, TaskProgramService>();
            services.AddScoped<ITaskProgramUsageService, TaskProgramUsageService>();
            services.AddScoped<ITrackedProgramService, TrackedProgramService>();
            services.AddScoped<IRestrictionService, RestrictionService>();
            services.AddScoped<IRestrictionNotificationService, RestrictionNotificationService>();
            services.AddScoped<IImpulseReminderService, ImpulseReminderService>();

            services.AddSingleton<INotificationService, MessageBoxNotificationService>();
            services.AddSingleton<ProgramNameResolverService>();
            services.AddSingleton<ProgramSyncService>();

            Services = services.BuildServiceProvider();
        }

        private void SetupTrayIcon()
        {
            _notifyIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = new System.Drawing.Icon("icon.ico"),
                Visible = true,
                Text = "FocusTracker",
                ContextMenuStrip = new System.Windows.Forms.ContextMenuStrip()
            };

            _notifyIcon.ContextMenuStrip.Items.Add("Відкрити", null, (s, e) =>
            {
                Current.MainWindow.Show();
                Current.MainWindow.WindowState = WindowState.Normal;
            });

            _notifyIcon.ContextMenuStrip.Items.Add("Вийти", null, (s, e) =>
            {
                _exitRequested = true;
                Current.MainWindow.Close();
            });

            _notifyIcon.DoubleClick += (s, e) =>
            {
                Current.MainWindow.Show();
                Current.MainWindow.WindowState = WindowState.Normal;
            };
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _notifyIcon?.Dispose();
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            base.OnExit(e);
        }
    }
}