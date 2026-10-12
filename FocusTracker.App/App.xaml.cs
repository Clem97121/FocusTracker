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
using FocusTracker.Data.Cloud;
using FocusTracker.Domain;
using FocusTracker.Domain.Interfaces;
using FocusTracker.Domain.Models;
using FocusTracker.App.Services;
using System.IO;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Threading;
using System.IO.Pipes;
using FocusTracker.App.Helpers;

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
        private UserActivityTracker? _userActivity;
        private UsageRecorder? _usageRecorder;
        private PresenceMonitor? _presence;
        private CancellationTokenSource? _flushCts;

        private Mutex? _mutex;
        private bool _ownsMutex;
        private string _dbPath = "";

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

        private const string StartupValueName = "FocusTracker";

        // Викликається Velopack перед видаленням застосунку
        public static void RemoveFromStartup()
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            key?.DeleteValue(StartupValueName, throwOnMissingValue: false);
        }

        private void AddToStartup()
        {
            string appName = StartupValueName;
            // Assembly.Location у .NET 8 вказує на .dll, а запускати треба .exe
            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;

            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;

            var expected = $"\"{exePath}\"";
            // Перезаписуємо і старий хибний шлях до .dll з попередніх версій
            if (!string.Equals(key.GetValue(appName) as string, expected, StringComparison.OrdinalIgnoreCase))
            {
                key.SetValue(appName, expected);
            }
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            const string mutexName = "FocusTrackerAppSingleton";
            bool createdNew;
            _mutex = new Mutex(true, mutexName, out createdNew);
            _ownsMutex = createdNew;

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

            SetupExceptionHandling();
            AppLog.Info("Запуск FocusTracker");

#if !DEBUG
            // Лише Release: dev-збірки з клону не повинні перехоплювати автозапуск
            try
            {
                AddToStartup();
            }
            catch (Exception ex)
            {
                AppLog.Error("Не вдалося додати в автозапуск", ex);
            }
#endif

            ConfigureServices();

            StartPipeServer();

            string? archivedDbPath;
            try
            {
                archivedDbPath = DatabaseInitializer.Initialize(
                    Services.GetRequiredService<IDbContextFactory<FocusTrackerDbContext>>(), _dbPath);
            }
            catch (Exception ex)
            {
                // Без бази застосунок не працює: не продовжуємо мовчки, а завершуємось з поясненням
                AppLog.Error("Не вдалося відкрити базу даних", ex);
                MessageBox.Show(
                    $"Не вдалося відкрити базу даних:\n{ex.Message}\n\nДеталі в лозі: {AppLog.LogDirectory}",
                    "FocusTracker", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            if (archivedDbPath != null)
            {
                AppLog.Info($"Стару несумісну базу перенесено в {archivedDbPath}, створено нову");
                MessageBox.Show(
                    $"Знайдено базу даних старої версії, несумісну з поточною. Її збережено як\n{archivedDbPath}\nі створено нову порожню базу.",
                    "FocusTracker", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            try
            {
                var backupPath = DatabaseBackup.CreateDailyBackup(
                    _dbPath, Path.Combine(Path.GetDirectoryName(_dbPath)!, "backups"), DateTime.Now);
                if (backupPath != null)
                    AppLog.Info($"Створено резервну копію бази: {backupPath}");
            }
            catch (Exception ex)
            {
                AppLog.Error("Не вдалося створити резервну копію бази", ex);
            }

            ShowUiNotification = message => new CustomMessageBox(message).ShowDialog();

            try
            {
                var syncService = Services.GetRequiredService<ProgramSyncService>();
                await syncService.SyncAsync();
            }
            catch (Exception ex)
            {
                AppLog.Error("Помилка синхронізації програм", ex);
            }

            _notificationTimer = new Timer(async _ =>
            {
                try
                {
                    var service = Services.GetRequiredService<IRestrictionNotificationService>();
                    await service.CheckAndNotifyAsync();
                }
                catch (Exception ex)
                {
                    AppLog.Error("Помилка перевірки попереджень про обмеження", ex);
                }
            }, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));

            _impulseTimer = new Timer(async _ =>
            {
                try
                {
                    var service = Services.GetRequiredService<IImpulseReminderService>();
                    await service.CheckAndRemindAsync();
                }
                catch (Exception ex)
                {
                    AppLog.Error("Помилка імпульсного нагадування", ex);
                }
            }, null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));

            _userActivity = new UserActivityTracker();
            _userActivity.Start();

            _usageRecorder = new UsageRecorder(Services);
            var userActivity = _userActivity;
            var usageRecorder = _usageRecorder;

            var programServiceForTracker = Services.GetRequiredService<IProgramService>();
            var nameResolver = Services.GetRequiredService<ProgramNameResolverService>();

            _presence = new PresenceMonitor(userActivity, programServiceForTracker, Services.GetRequiredService<SettingsStore>());
            var presence = _presence;

            // Щосекунди лише додаємо час у буфер у пам'яті, без звернень до БД.
            // Коли користувача немає за пристроєм, час не пишемо зовсім.
            _windowTracker = new ActiveWindowTracker(
                (appName, totalTime) =>
                {
                    if (!presence.IsPresent(appName)) return;

                    var activeTime = userActivity.GetActivePart(totalTime);
                    usageRecorder.Add(appName, totalTime, activeTime);
                },
                programServiceForTracker,
                nameResolver
            );
            _windowTracker.Error += ex => AppLog.Error("Помилка трекера вікон", ex);

            _windowTracker.Start();
            StartUsageFlushLoop();
            SetupTrayIcon();

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();

            StartUpdateChecks(mainWindow);
            Services.GetRequiredService<CloudSyncRunner>().Start(_flushCts!.Token);
        }

        private void StartUpdateChecks(MainWindow mainWindow)
        {
            var updates = Services.GetRequiredService<UpdateService>();
            updates.UpdateAvailable += version => Dispatcher.Invoke(() =>
            {
                mainWindow.ShowUpdateBanner(version);
                _notifyIcon.ShowBalloonTip(10000, "FocusTracker",
                    $"Доступна нова версія {version}. Відкрийте застосунок, щоб оновити.",
                    System.Windows.Forms.ToolTipIcon.Info);
            });
            updates.StartPeriodicChecks(_flushCts!.Token);
        }

        // Завантажує оновлення і закриває застосунок; Velopack встановить нову версію і запустить її
        public async Task InstallUpdateAsync(Action<int>? progress = null)
        {
            await Services.GetRequiredService<UpdateService>().DownloadAndPrepareRestartAsync(progress);
            _exitRequested = true;
            Current.MainWindow.Close();
        }

        // Раз на кілька секунд записуємо накопичений час у БД і перевіряємо обмеження
        private void StartUsageFlushLoop()
        {
            _flushCts = new CancellationTokenSource();
            var token = _flushCts.Token;

            Task.Run(async () =>
            {
                using var timer = new PeriodicTimer(UsageRecorder.FlushInterval);
                try
                {
                    while (await timer.WaitForNextTickAsync(token))
                    {
                        try
                        {
                            await _presence!.RefreshIgnoreIdleAsync();
                            await _usageRecorder!.FlushAsync();
                            await _usageRecorder.EnforceRestrictionsAsync(_windowTracker.CurrentApp);
                        }
                        catch (Exception ex)
                        {
                            AppLog.Error("Помилка запису статистики", ex);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                }
            });
        }

        private void SetupExceptionHandling()
        {
            DispatcherUnhandledException += (s, args) =>
            {
                AppLog.Error("Необроблена помилка в UI", args.Exception);
                args.Handled = true;
                try
                {
                    MessageBox.Show(
                        $"Сталася помилка: {args.Exception.Message}\n\nДеталі в лозі: {AppLog.LogDirectory}",
                        "FocusTracker", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch { }
            };

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
                AppLog.Error("Критична помилка", args.ExceptionObject as Exception);

            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                AppLog.Error("Необроблена помилка у фоновій задачі", args.Exception);
                args.SetObserved();
            };
        }

        private void ConfigureServices()
        {
            var services = new ServiceCollection();

            var dbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FocusTracker",
                "focus_tracker.db");
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
            _dbPath = dbPath;

            // Кожна операція відкриває власний короткоживучий DbContext через фабрику,
            // тож сервіси не тримають стан і можуть бути singleton
            services.AddDbContextFactory<FocusTrackerDbContext>(options =>
                options.UseSqlite($"Data Source={dbPath};Default Timeout=30"));

            // Екрани бачать статистику всіх пристроїв (за вибраним StatsScope), запис іде в локальну базу
            services.AddSingleton<AppUsageStatService>();
            services.AddSingleton<StatsScope>();
            services.AddSingleton<IAppUsageStatService, CombinedAppUsageStatService>();
            services.AddSingleton<CloudSyncService>();
            services.AddSingleton<CloudSyncRunner>();
            services.AddSingleton<IRestrictionEvaluator, RestrictionEvaluator>();
            services.AddSingleton<IProgramService, ProgramService>();
            services.AddSingleton<ISkillService, SkillService>();
            services.AddSingleton<ISkillCategoryService, SkillCategoryService>();
            services.AddSingleton<ITaskItemService, TaskItemService>();
            services.AddSingleton<ITaskProgramService, TaskProgramService>();
            services.AddSingleton<ITaskProgramUsageService, TaskProgramUsageService>();
            services.AddSingleton<ITaskCompletionService, TaskCompletionService>();
            services.AddSingleton<ITrackedProgramService, TrackedProgramService>();
            services.AddSingleton<IRestrictionService, RestrictionService>();
            services.AddSingleton<IRestrictionNotificationService, RestrictionNotificationService>();
            services.AddSingleton<IImpulseReminderService, ImpulseReminderService>();

            services.AddSingleton<INotificationService, MessageBoxNotificationService>();
            services.AddSingleton<ProgramNameResolverService>();
            services.AddSingleton<ProgramSyncService>();
            services.AddSingleton<UpdateService>();
            services.AddSingleton(new SettingsStore(Path.Combine(Path.GetDirectoryName(dbPath)!, "settings.json")));

            Services = services.BuildServiceProvider();
        }

        private void SetupTrayIcon()
        {
            _notifyIcon = new System.Windows.Forms.NotifyIcon
            {
                // Шлях від папки з exe: при автозапуску робоча папка — System32
                Icon = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "icon.ico")),
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

            _notifyIcon.BalloonTipClicked += (s, e) =>
            {
                Current.MainWindow.Show();
                Current.MainWindow.WindowState = WindowState.Normal;
                Current.MainWindow.Activate();
            };
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                _windowTracker?.Stop();
                _flushCts?.Cancel();
                _userActivity?.Stop();
                _notificationTimer?.Dispose();
                _impulseTimer?.Dispose();
                // Дописуємо в БД те, що лишилось у буфері
                // Task.Run: без UI-контексту, інакше Wait на UI-потоці може зависнути
                if (_usageRecorder != null)
                    Task.Run(() => _usageRecorder.FlushAsync()).Wait(TimeSpan.FromSeconds(5));
                AppLog.Info("Завершення FocusTracker");
            }
            catch (Exception ex)
            {
                AppLog.Error("Помилка під час завершення", ex);
            }

            _notifyIcon?.Dispose();
            // Друга копія м'ютексом не володіє, і ReleaseMutex у ній кидав виняток
            if (_ownsMutex)
                _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            base.OnExit(e);
        }
    }
}