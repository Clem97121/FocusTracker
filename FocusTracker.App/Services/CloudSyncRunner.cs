using FocusTracker.App.Helpers;
using FocusTracker.Data;
using FocusTracker.Data.Cloud;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace FocusTracker.App.Services
{
    // Раз на хвилину синхронізує статистику з хмарною базою, якщо синхронізацію увімкнено
    public class CloudSyncRunner
    {
        public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

        private readonly CloudSyncService _sync;
        private readonly SettingsStore _settings;
        private string? _lastLoggedError;

        public CloudSyncRunner(CloudSyncService sync, SettingsStore settings)
        {
            _sync = sync;
            _settings = settings;
        }

        public DateTime? LastSuccessAt { get; private set; }
        public SyncResult? LastResult { get; private set; }
        public string? LastError { get; private set; }

        // Стан змінився (успіх або помилка). Викликається не з UI-потоку.
        public event Action? StatusChanged;

        public bool IsConfigured =>
            _settings.Current.CloudSyncEnabled && CloudCredentials.Unprotect(_settings.Current.CloudConnectionProtected) != null;

        public void Start(CancellationToken token)
        {
            Task.Run(async () =>
            {
                using var timer = new PeriodicTimer(Interval);
                try
                {
                    do
                    {
                        await SyncNowAsync(token);
                    }
                    while (await timer.WaitForNextTickAsync(token));
                }
                catch (OperationCanceledException)
                {
                }
            });
        }

        public async Task SyncNowAsync(CancellationToken token = default)
        {
            var settings = _settings.Current;
            var connection = CloudCredentials.Unprotect(settings.CloudConnectionProtected);
            if (!settings.CloudSyncEnabled || connection == null) return;

            try
            {
                LastResult = await _sync.SyncAsync(new CloudStore(connection), settings.DeviceId, settings.DeviceName, token);
                LastSuccessAt = DateTime.Now;
                LastError = null;
                _lastLoggedError = null;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                // Без інтернету помилка повторюється щохвилини — пишемо в лог лише нову
                if (_lastLoggedError != ex.Message)
                {
                    _lastLoggedError = ex.Message;
                    AppLog.Error("Помилка синхронізації з хмарою", ex);
                }
            }

            StatusChanged?.Invoke();
        }
    }
}
