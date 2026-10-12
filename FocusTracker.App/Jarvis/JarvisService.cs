using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;
using FocusTracker.App.Helpers;
using FocusTracker.Domain;
using FocusTracker.App.Services;
using FocusTracker.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FocusTracker.App.Jarvis
{
    public record JarvisNote(DateTime At, string Kind, string Text);

    public record JarvisDecision(string Action, string Message, int FocusMinutes, List<string> BlockApps);

    public enum JarvisCheckKind { Check, Morning, Evening }

    // Асистент продуктивності: періодично оцінює ситуацію через Claude і втручається,
    // а також відповідає в чаті
    public class JarvisService
    {
        private const string SystemPrompt = """
            Ти — Джарвіс, особистий асистент продуктивності користувача в застосунку FocusTracker.
            Ти бачиш, які програми й вікна він використовує на своїх пристроях, його цілі з пріоритетами,
            задачі та розклад. Твоє завдання — допомагати йому робити те, що наближає до цілей,
            і помічати прокрастинацію, зокрема «продуктивну»: корисну на вигляд роботу, яка зараз
            не є пріоритетом (наприклад, пиляти власний пет-проєкт, коли ціль №1 — відгуки на вакансії).

            Як діяти:
            - Оцінюй поведінку відносно цілей і їхнього порядку. Відпочинок і особистий час поза
              продуктивними годинами — нормально, не втручайся, якщо правила користувача не кажуть інакше.
            - Не набридай. Якщо все добре або ти нещодавно вже казав про те саме, а ситуація не змінилась
              суттєво — нічого не роби. Про ту саму річ — не частіше ніж раз на ~40 хвилин.
            - Будь коротким (1–3 речення), конкретним і по-дружньому прямим; трохи гумору доречно.
              Пропонуй один конкретний наступний крок на 15–30 хвилин.
            - Наростай поступово, дивлячись на свої попередні повідомлення: м'яко нагадати → спитати,
              що він робить і навіщо → запропонувати конкретний план → фокус-режим.
            - Фокус-режим — лише якщо його дозволено, а користувач ігнорує попередні нагадування або
              цього вимагає його правило. Блокуй тільки явно відволікаючі програми (ігри, Steam, Discord тощо),
              вказуючи ідентифікатори процесів у дужках із контексту. Ніколи не блокуй FocusTracker,
              а браузер і робочі інструменти — якщо вони потрібні для цілей.
            - Пиши тією мовою, якою користувач написав свої цілі.
            """;

        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        private readonly JarvisContext _context;
        private readonly SettingsStore _settings;
        private readonly FocusMode _focus;
        private readonly object _lock = new();
        private readonly List<JarvisNote> _notes = new();
        private readonly SemaphoreSlim _gate = new(1, 1);
        private DateTime _lastCheck = DateTime.MinValue;
        private DateTime _morningDoneFor = DateTime.MinValue;
        private DateTime _eveningDoneFor = DateTime.MinValue;

        public JarvisService(JarvisContext context, SettingsStore settings, FocusMode focus)
        {
            _context = context;
            _settings = settings;
            _focus = focus;
        }

        // Нове повідомлення Джарвіса (втручання або відповідь у чаті). Викликається не з UI-потоку.
        public event Action<JarvisNote>? NoteAdded;

        public string? LastError { get; private set; }
        public DateTime? LastCheckAt { get; private set; }

        public IReadOnlyList<JarvisNote> Notes
        {
            get { lock (_lock) return _notes.ToList(); }
        }

        public bool IsConfigured =>
            _settings.Current.JarvisEnabled && CloudCredentials.Unprotect(_settings.Current.AnthropicKeyProtected) != null;

        // Щохвилини: ранковий план, перевірки в продуктивні години, вечірній підсумок
        public void Start(Func<bool> isUserPresent, CancellationToken token)
        {
            Task.Run(async () =>
            {
                using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
                try
                {
                    while (await timer.WaitForNextTickAsync(token))
                    {
                        try
                        {
                            await TickAsync(isUserPresent(), token);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            AppLog.Error("Помилка Джарвіса", ex);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                }
            });
        }

        private async Task TickAsync(bool present, CancellationToken token)
        {
            if (!IsConfigured || !present) return;

            var s = _settings.Current;
            var now = DateTime.Now;
            var inProductiveHours = now.Hour >= s.ProductiveFromHour && now.Hour < s.ProductiveToHour;

            if (inProductiveHours && _morningDoneFor != DateTime.Today)
            {
                _morningDoneFor = DateTime.Today;
                await CheckAsync(JarvisCheckKind.Morning, token);
            }
            else if (inProductiveHours && now - _lastCheck >= TimeSpan.FromMinutes(s.JarvisCheckMinutes))
            {
                await CheckAsync(JarvisCheckKind.Check, token);
            }
            else if (!inProductiveHours && now.Hour >= s.ProductiveToHour
                && _morningDoneFor == DateTime.Today && _eveningDoneFor != DateTime.Today)
            {
                _eveningDoneFor = DateTime.Today;
                await CheckAsync(JarvisCheckKind.Evening, token);
            }
        }

        public async Task<JarvisDecision?> CheckAsync(JarvisCheckKind kind, CancellationToken token = default)
        {
            await _gate.WaitAsync(token);
            try
            {
                _lastCheck = DateTime.Now;
                var task = kind switch
                {
                    JarvisCheckKind.Morning => "Почалися продуктивні години. Коротко привітайся і запропонуй план на сьогодні: 2–4 конкретні кроки під цілі з урахуванням задач. action = plan.",
                    JarvisCheckKind.Evening => "Продуктивні години закінчились. Чесно й коротко підсумуй день: що зроблено для цілей, що ні, і одна порада на завтра. action = plan.",
                    _ => "Оціни, що відбувається, і виріши, чи потрібно втрутитися зараз. Якщо ні — action = none."
                };

                var response = await CreateAsync(new MessageCreateParams
                {
                    Model = _settings.Current.JarvisModel,
                    MaxTokens = 4000,
                    System = SystemWithProfile(),
                    OutputConfig = new BetaOutputConfig
                    {
                        Effort = Effort.Low,
                        Format = new BetaJsonOutputFormat { Schema = DecisionSchema() },
                    },
                    Messages = [new() { Role = Role.User, Content = await _context.DescribeNowAsync(Notes) + "\n\n## Завдання\n" + task }],
                }, token);
                if (response == null) return null;

                var decision = JsonSerializer.Deserialize<JarvisDecision>(Text(response), JsonOptions);
                LastCheckAt = DateTime.Now;
                if (decision == null) return null;

                AppLog.Info($"Джарвіс ({kind}): {decision.Action} {decision.Message}");
                Apply(decision);
                return decision;
            }
            finally
            {
                _gate.Release();
            }
        }

        // Відповідь у чаті; history — попередні репліки (user/assistant) по черзі
        public async Task<string?> ChatAsync(IReadOnlyList<(bool FromUser, string Text)> history, CancellationToken token = default)
        {
            // Кілька реплік підряд від одного боку (напр. нагадування Джарвіса) зливаємо в одну
            var merged = new List<(bool FromUser, string Text)>();
            foreach (var item in history.TakeLast(30))
            {
                if (merged.Count > 0 && merged[^1].FromUser == item.FromUser)
                    merged[^1] = (item.FromUser, merged[^1].Text + "\n\n" + item.Text);
                else
                    merged.Add(item);
            }
            while (merged.Count > 0 && !merged[0].FromUser)
                merged.RemoveAt(0);

            var messages = merged
                .Select(m => new BetaMessageParam { Role = m.FromUser ? Role.User : Role.Assistant, Content = m.Text })
                .ToList();

            var system = SystemWithProfile();
            system.Add(new BetaTextBlockParam
            {
                Text = "Зараз ти в чаті з користувачем. Відповідай коротко і по суті, без JSON.\n\n"
                    + await _context.DescribeNowAsync(Notes)
            });

            var response = await CreateAsync(new MessageCreateParams
            {
                Model = _settings.Current.JarvisModel,
                MaxTokens = 4000,
                System = system,
                Messages = messages,
            }, token);
            if (response == null) return null;

            var reply = Text(response).Trim();
            AddNote(new JarvisNote(DateTime.Now, "chat", reply), notify: false);
            return reply;
        }

        private void Apply(JarvisDecision decision)
        {
            if (decision.Action == "none" || string.IsNullOrWhiteSpace(decision.Message)) return;

            if (decision.Action == "focus" && _settings.Current.JarvisMayStartFocus && decision.FocusMinutes > 0 && decision.BlockApps.Count > 0)
            {
                var apps = decision.BlockApps.Where(a => !a.Equals("FocusTracker.App", StringComparison.OrdinalIgnoreCase));
                _focus.Start(TimeSpan.FromMinutes(Math.Min(decision.FocusMinutes, 180)), apps, decision.Message);
            }

            AddNote(new JarvisNote(DateTime.Now, decision.Action, decision.Message), notify: true);
        }

        private void AddNote(JarvisNote note, bool notify)
        {
            lock (_lock)
            {
                _notes.Add(note);
                _notes.RemoveAll(n => n.At.Date < DateTime.Today);
            }
            if (notify)
                NoteAdded?.Invoke(note);
        }

        private List<BetaTextBlockParam> SystemWithProfile() =>
        [
            new() { Text = SystemPrompt },
            new() { Text = _context.DescribeProfile(), CacheControl = new BetaCacheControlEphemeral() },
        ];

        // Запит до Claude з резервною моделлю на випадок відмови класифікатора безпеки
        private async Task<BetaMessage?> CreateAsync(MessageCreateParams parameters, CancellationToken token)
        {
            var key = CloudCredentials.Unprotect(_settings.Current.AnthropicKeyProtected);
            if (key == null) return null;

            var client = new AnthropicClient { ApiKey = key };
            if (SupportsFallbacks(parameters.Model))
            {
                // "default": сервер сам обирає резервну модель за категорією відмови
                parameters = parameters with
                {
                    Betas = [AnthropicBeta.ServerSideFallback2026_07_01],
                    Fallbacks = new Default(),
                };
            }

            try
            {
                var response = await client.Beta.Messages.Create(parameters, cancellationToken: token);
                LastError = null;
                if (response.StopReason == "refusal")
                {
                    LastError = "Claude відмовився відповідати на цей запит.";
                    return null;
                }
                return response;
            }
            catch (AnthropicApiException ex)
            {
                LastError = ex.Message;
                AppLog.Error("Помилка запиту до Claude", ex);
                return null;
            }
        }

        private static bool SupportsFallbacks(string model) =>
            model.StartsWith("claude-opus", StringComparison.Ordinal) || model.StartsWith("claude-sonnet", StringComparison.Ordinal)
            || model.StartsWith("claude-fable", StringComparison.Ordinal);

        private static string Text(BetaMessage response) =>
            string.Concat(response.Content.Select(b => b.TryPickText(out var t) ? t.Text : string.Empty));

        private static Dictionary<string, JsonElement> DecisionSchema() => new()
        {
            ["type"] = JsonSerializer.SerializeToElement("object"),
            ["properties"] = JsonSerializer.SerializeToElement(new
            {
                action = new { type = "string", @enum = new[] { "none", "nudge", "ask", "plan", "focus" } },
                message = new { type = "string", description = "Повідомлення користувачу; порожнє для none" },
                focusMinutes = new { type = "integer", description = "Тривалість фокус-режиму; 0, якщо не focus" },
                blockApps = new { type = "array", items = new { type = "string" }, description = "Ідентифікатори процесів для блокування" },
            }),
            ["required"] = JsonSerializer.SerializeToElement(new[] { "action", "message", "focusMinutes", "blockApps" }),
            ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
        };
    }
}
