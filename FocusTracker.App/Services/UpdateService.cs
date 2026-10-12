using FocusTracker.App.Helpers;
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace FocusTracker.App.Services
{
    // Перевіряє нові версії в GitHub Releases і встановлює їх через Velopack.
    // Працює лише у встановленій копії (Setup.exe); у dev-збірці нічого не робить.
    public class UpdateService
    {
        public const string RepositoryUrl = "https://github.com/Clem97121/FocusTracker";
        public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

        private readonly UpdateManager _manager = new(new GithubSource(RepositoryUrl, null, false));
        private readonly SemaphoreSlim _gate = new(1, 1);
        private UpdateInfo? _available;

        // Знайдено нову версію (номер версії). Викликається не з UI-потоку.
        public event Action<string>? UpdateAvailable;

        public bool IsInstalled => _manager.IsInstalled;

        public string CurrentVersion =>
            _manager.CurrentVersion?.ToString()
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
            ?? "?";

        public string? AvailableVersion => _available?.TargetFullRelease.Version.ToString();

        // Повертає номер нової версії або null, якщо оновлень немає
        public async Task<string?> CheckAsync()
        {
            if (!IsInstalled) return null;

            await _gate.WaitAsync();
            try
            {
                var info = await _manager.CheckForUpdatesAsync();
                if (info == null) return null;

                var isNew = _available?.TargetFullRelease.Version != info.TargetFullRelease.Version;
                _available = info;
                var version = info.TargetFullRelease.Version.ToString();
                if (isNew)
                {
                    AppLog.Info($"Доступна нова версія {version}");
                    UpdateAvailable?.Invoke(version);
                }
                return version;
            }
            finally
            {
                _gate.Release();
            }
        }

        // Завантажує оновлення і готує його до встановлення після виходу з застосунку.
        // Після цього треба завершити застосунок: Velopack замінить файли й запустить нову версію.
        public async Task DownloadAndPrepareRestartAsync(Action<int>? progress = null)
        {
            var info = _available ?? throw new InvalidOperationException("Немає доступного оновлення");

            await _manager.DownloadUpdatesAsync(info, progress);
            _manager.WaitExitThenApplyUpdates(info.TargetFullRelease, silent: false, restart: true, restartArgs: null);
            AppLog.Info($"Оновлення до {info.TargetFullRelease.Version} буде встановлено після виходу");
        }

        // Перевірка при старті (із затримкою, щоб не гальмувати запуск) і далі раз на CheckInterval
        public void StartPeriodicChecks(CancellationToken token)
        {
            if (!IsInstalled) return;

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), token);
                    using var timer = new PeriodicTimer(CheckInterval);
                    do
                    {
                        try
                        {
                            await CheckAsync();
                        }
                        catch (Exception ex)
                        {
                            AppLog.Error("Не вдалося перевірити оновлення", ex);
                        }
                    }
                    while (await timer.WaitForNextTickAsync(token));
                }
                catch (OperationCanceledException)
                {
                }
            });
        }
    }
}
