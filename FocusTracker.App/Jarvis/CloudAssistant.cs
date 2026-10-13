using FocusTracker.App.Helpers;
using FocusTracker.App.Services;
using FocusTracker.Data;
using FocusTracker.Data.Cloud;
using FocusTracker.Domain;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FocusTracker.App.Jarvis
{
    // Повідомлення Коуча для віконця в кутку
    public record JarvisNote(DateTime At, string Kind, string Text);

    // Коуч живе в хмарі: перевірки й чат виконує функція Supabase (supabase/functions/assistant).
    // Застосунок сам налаштовує базу, щоб вона будила функцію, відправляє їй присутність
    // і недавню активність, показує її повідомлення й застосовує спільний для всіх пристроїв фокус-режим.
    public class CloudAssistant
    {
        public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan UploadInterval = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan ReplyPollInterval = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(90);
        // Втручання, які прийшли, поки застосунок був вимкнений, у кутку вже не показуємо
        private static readonly TimeSpan PopupMaxAge = TimeSpan.FromMinutes(15);
        // Після невдалого налаштування бази пробуємо знову не одразу, а через деякий час
        private static readonly TimeSpan SetupRetryInterval = TimeSpan.FromMinutes(5);

        private const string LastSeenKey = "assistant.last_seen_message_id";

        private readonly SettingsStore _settings;
        private readonly CloudSyncService _sync;
        private readonly RecentActivityLog _recent;
        private readonly FocusMode _focus;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private DateTime _lastUploadAt = DateTime.MinValue;
        private DateTime _recentWatermark = DateTime.MinValue;
        private DateTime? _appliedFocusUpdatedAt;
        private bool _focusFromCloud;
        private string? _lastLoggedError;
        private DateTime _setupRetryAt = DateTime.MinValue;

        public CloudAssistant(SettingsStore settings, CloudSyncService sync, RecentActivityLog recent, FocusMode focus)
        {
            _settings = settings;
            _sync = sync;
            _recent = recent;
            _focus = focus;
        }

        // Нове повідомлення в спільному чаті (будь-яке, і від користувача теж). Викликається не з UI-потоку.
        public event Action<CloudAssistantMessage>? MessageReceived;

        // Втручання асистента для цього пристрою: показати в кутку. Викликається не з UI-потоку.
        public event Action<JarvisNote>? NoteAdded;

        // Помилка зв'язку з хмарою (не самої функції)
        public string? LastError { get; private set; }

        // Не вдалося налаштувати розклад і тригер у базі; повторимо пізніше
        public string? SetupError { get; private set; }

        // Стан функції в хмарі: коли запускалась і чи була помилка
        public CloudAssistantState? State { get; private set; }

        // Коуч працює лише через хмарну базу
        public bool IsCloudConfigured => _settings.Current is { CloudSyncEnabled: true } s
            && CloudCredentials.Unprotect(s.CloudConnectionProtected) != null;

        // Коуч діє, коли його увімкнено й налаштовано синхронізацію з хмарою
        public bool IsActive => _settings.Current.JarvisEnabled && IsCloudConfigured;

        public void Start(Func<(DateTime LastInput, string App)> presence, CancellationToken token)
        {
            Task.Run(async () =>
            {
                using var timer = new PeriodicTimer(PollInterval);
                try
                {
                    while (await timer.WaitForNextTickAsync(token))
                    {
                        if (!IsActive) continue;
                        await TickAsync(presence(), token);
                    }
                }
                catch (OperationCanceledException)
                {
                }
            });
        }

        private async Task TickAsync((DateTime LastInput, string App) presence, CancellationToken token)
        {
            await _gate.WaitAsync(token);
            try
            {
                var store = Store();
                if (store == null) return;

                await EnsureSetupAsync(store, token);

                if (DateTime.Now - _lastUploadAt >= UploadInterval - TimeSpan.FromSeconds(5))
                {
                    await UploadActivityAsync(store, presence, token);
                    _lastUploadAt = DateTime.Now;
                }
                await PollMessagesAsync(store, token);
                await ApplyFocusAsync(store, token);
                State = await store.GetAssistantStateAsync(token);

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
                // Без інтернету помилка повторюється щоразу — пишемо в лог лише нову
                if (_lastLoggedError != ex.Message)
                {
                    _lastLoggedError = ex.Message;
                    AppLog.Error("Помилка зв'язку з асистентом у хмарі", ex);
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        // Розклад pg_cron і тригер чату, що будять функцію, ставимо самі — раз на пристрій і версію.
        // Помилка не заважає решті: пишемо в лог і пробуємо знову пізніше.
        private async Task EnsureSetupAsync(CloudStore store, CancellationToken token)
        {
            if (DateTime.Now < _setupRetryAt) return;
            try
            {
                var configured = await _sync.EnsureAssistantSetupAsync(_settings.Current.DeviceId, t =>
                {
                    var functionUrl = SupabaseProject.GetFunctionUrl(CloudCredentials.Unprotect(_settings.Current.CloudConnectionProtected))
                        ?? throw new InvalidOperationException("Не вдалося визначити проєкт Supabase з рядка підключення. Потрібен Session pooler (користувач postgres.<ref>) або пряме підключення до db.<ref>.supabase.co.");
                    return store.SetupAssistantAsync(functionUrl, t);
                }, token);
                if (configured)
                    AppLog.Info($"{AssistantInfo.Name}: базу налаштовано, функція викликатиметься раз на 5 хвилин і на кожне повідомлення в чаті.");
                SetupError = null;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _setupRetryAt = DateTime.Now + SetupRetryInterval;
                if (SetupError != ex.Message)
                    AppLog.Error($"Не вдалося налаштувати {AssistantInfo.Name} у хмарі", ex);
                SetupError = ex.Message;
            }
        }

        // Присутність і що було у фокусі — те, з чого функція дізнається про активний пристрій
        private async Task UploadActivityAsync(CloudStore store, (DateTime LastInput, string App) presence, CancellationToken token)
        {
            var s = _settings.Current;
            await store.UpsertPresenceAsync(s.DeviceId, s.DeviceName, presence.LastInput.ToUniversalTime(),
                string.IsNullOrWhiteSpace(presence.App) ? null : presence.App, token);

            // Мітку беремо з запасом: подовжений запис перезапишеться, а не задублюється
            var startedAt = DateTime.Now;
            var items = _recent.ChangedSince(_recentWatermark)
                .Select(i => new CloudRecentActivity(i.At.ToUniversalTime(), i.Duration.TotalSeconds, i.App, i.Title))
                .ToList();
            await store.UploadRecentActivityAsync(s.DeviceId, items, token);
            _recentWatermark = startedAt - TimeSpan.FromSeconds(5);
        }

        private async Task PollMessagesAsync(CloudStore store, CancellationToken token)
        {
            // Уперше (або після зміни бази) не показуємо стару історію, лише нові повідомлення
            if (!long.TryParse(await _sync.GetStateAsync(LastSeenKey), NumberStyles.Integer, CultureInfo.InvariantCulture, out var lastSeen))
            {
                await _sync.SetStateAsync(LastSeenKey, (await store.GetLastMessageIdAsync(token)).ToString(CultureInfo.InvariantCulture));
                return;
            }

            var messages = await store.GetMessagesAfterAsync(lastSeen, token);
            if (messages.Count == 0) return;
            await _sync.SetStateAsync(LastSeenKey, messages[^1].Id.ToString(CultureInfo.InvariantCulture));

            var deviceId = _settings.Current.DeviceId;
            foreach (var message in messages)
            {
                MessageReceived?.Invoke(message);

                if (message.Role == "assistant" && message.Kind != "chat"
                    && (message.TargetDeviceId == null || message.TargetDeviceId == deviceId)
                    && DateTime.UtcNow - message.CreatedAtUtc < PopupMaxAge)
                {
                    NoteAdded?.Invoke(ToNote(message));
                }
            }
        }

        // Один фокус-режим на всі пристрої: вмикаємо локально те, що в хмарі, і вимикаємо, коли там його зняли
        private async Task ApplyFocusAsync(CloudStore store, CancellationToken token)
        {
            var focus = await store.GetFocusAsync(token);
            if (focus == null) return;

            var apps = focus.Apps
                .Where(a => !a.Equals("FocusTracker.App", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (focus.UntilUtc is { } until && until > DateTime.UtcNow && apps.Count > 0)
            {
                if (_appliedFocusUpdatedAt == focus.UpdatedAtUtc) return;
                _focus.Start(until - DateTime.UtcNow, apps, focus.Reason);
                _appliedFocusUpdatedAt = focus.UpdatedAtUtc;
                _focusFromCloud = true;
            }
            else if (_focusFromCloud)
            {
                if (_focus.IsActive) _focus.Stop();
                _focusFromCloud = false;
                _appliedFocusUpdatedAt = null;
            }
        }

        // «Завершити фокус-режим»: тут і в хмарі, тобто на всіх пристроях
        public async Task StopFocusAsync()
        {
            await _gate.WaitAsync();
            try
            {
                _focus.Stop();
                _focusFromCloud = false;
                _appliedFocusUpdatedAt = null;
                if (Store() is { } store)
                    await store.ClearFocusAsync();
            }
            finally
            {
                _gate.Release();
            }
        }

        // Отримати нові повідомлення зараз, не чекаючи таймера
        public async Task PollNowAsync()
        {
            await _gate.WaitAsync();
            try
            {
                if (Store() is { } store)
                    await PollMessagesAsync(store, CancellationToken.None);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<List<CloudAssistantMessage>> LoadHistoryAsync(int count = 50)
        {
            var store = Store() ?? throw new InvalidOperationException("Синхронізацію з хмарою не налаштовано.");
            return await store.GetRecentMessagesAsync(count);
        }

        // Повідомлення в чат; відповідь пише функція, яку будить тригер у базі
        public async Task<long> SendAsync(string text)
        {
            var store = Store() ?? throw new InvalidOperationException("Синхронізацію з хмарою не налаштовано.");
            return await store.InsertUserMessageAsync(_settings.Current.DeviceId, text);
        }

        // Чекає, поки функція обробить повідомлення; null — не дочекались
        public async Task<CloudAssistantMessage?> WaitForReplyAsync(long messageId, CancellationToken token = default)
        {
            var store = Store() ?? throw new InvalidOperationException("Синхронізацію з хмарою не налаштовано.");
            var deadline = DateTime.UtcNow + ReplyTimeout;
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(ReplyPollInterval, token);
                var message = await store.GetMessageAsync(messageId, token);
                if (message != null && message.Status != "pending")
                    return message;
            }
            return null;
        }

        // Налаштування асистента для функції; відправляються при збереженні
        public async Task UploadSettingsAsync()
        {
            var store = Store() ?? throw new InvalidOperationException("Синхронізацію з хмарою не налаштовано.");
            await store.UploadAssistantSettingsAsync(AssistantCloudSettings.ToJson(_settings.Current, TimeZoneInfo.Local));
        }

        public static JarvisNote ToNote(CloudAssistantMessage message) =>
            new(message.CreatedAtUtc.ToLocalTime(), message.Kind, message.Text);

        private CloudStore? Store()
        {
            var s = _settings.Current;
            var connection = CloudCredentials.Unprotect(s.CloudConnectionProtected);
            return s.CloudSyncEnabled && connection != null ? new CloudStore(connection) : null;
        }
    }
}
