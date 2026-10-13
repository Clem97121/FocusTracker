using Npgsql;
using NpgsqlTypes;

namespace FocusTracker.Data.Cloud
{
    // Повідомлення спільного чату з асистентом; ними ж приходять його втручання (kind ≠ chat).
    // DeviceId — хто надіслав (для повідомлень користувача), TargetDeviceId — кому (null — усім).
    public record CloudAssistantMessage(long Id, DateTime CreatedAtUtc, string Role, string Kind, string Text,
        Guid? DeviceId, Guid? TargetDeviceId, string Status, string? Error);

    // Один фокус-режим на всі пристрої; UntilUtc = null — вимкнено
    public record CloudFocus(DateTime? UntilUtc, string Reason, string[] Apps, DateTime UpdatedAtUtc);

    public record CloudAssistantState(DateTime? LastRunAtUtc, DateTime? LastCheckAtUtc, string? LastError);

    // Відрізок часу в одному вікні; AtUtc — початок, і разом із програмою він ідентифікує запис
    public record CloudRecentActivity(DateTime AtUtc, double Seconds, string App, string Title);

    // Таблиці «мозку» асистента у хмарі. Самі перевірки й відповіді робить функція Supabase
    // supabase/functions/assistant, а застосунок лише постачає їй дані й показує результат.
    public partial class CloudStore
    {
        public const string CronJobName = "ft-assistant";

        public async Task UploadAssistantSettingsAsync(string json, CancellationToken token = default)
        {
            await using var connection = await OpenAsync(token);
            await using var command = new NpgsqlCommand("""
                insert into public.ft_assistant_settings (id, data, updated_at) values (1, @data::jsonb, now())
                on conflict (id) do update set data = excluded.data, updated_at = now();
                """, connection);
            command.Parameters.AddWithValue("data", json);
            await command.ExecuteNonQueryAsync(token);
        }

        public async Task UpsertPresenceAsync(Guid deviceId, string name, DateTime lastInputUtc, string? foregroundApp,
            CancellationToken token = default)
        {
            await using var connection = await OpenAsync(token);
            await using var command = new NpgsqlCommand("""
                insert into public.ft_presence (device_id, name, last_input_at, foreground_app, updated_at)
                values (@id, @name, @input, @app, now())
                on conflict (device_id) do update
                set name = excluded.name, last_input_at = excluded.last_input_at,
                    foreground_app = excluded.foreground_app, updated_at = now();
                """, connection);
            command.Parameters.AddWithValue("id", deviceId);
            command.Parameters.AddWithValue("name", name);
            command.Parameters.Add(new NpgsqlParameter("input", NpgsqlDbType.TimestampTz) { Value = DateTime.SpecifyKind(lastInputUtc, DateTimeKind.Utc) });
            command.Parameters.Add(new NpgsqlParameter("app", NpgsqlDbType.Text) { Value = (object?)foregroundApp ?? DBNull.Value });
            await command.ExecuteNonQueryAsync(token);
        }

        // Подовжений запис перезаписує тривалість, тож повторна відправка нічого не дублює
        public async Task UploadRecentActivityAsync(Guid deviceId, IReadOnlyList<CloudRecentActivity> items, CancellationToken token = default)
        {
            // В одному insert … on conflict той самий ключ не може трапитись двічі
            var unique = items
                .GroupBy(i => (i.AtUtc, i.App))
                .Select(g => g.Last())
                .ToList();
            if (unique.Count == 0) return;

            await using var connection = await OpenAsync(token);
            await using var command = new NpgsqlCommand("""
                insert into public.ft_recent_activity (device_id, at, seconds, app, title)
                select @device, u.at, u.seconds, u.app, u.title
                from unnest(@at, @seconds, @apps, @titles) as u(at, seconds, app, title)
                on conflict (device_id, at, app) do update
                set seconds = excluded.seconds, title = excluded.title;
                """, connection);
            command.Parameters.AddWithValue("device", deviceId);
            command.Parameters.Add(new NpgsqlParameter("at", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz)
            {
                Value = unique.Select(i => DateTime.SpecifyKind(i.AtUtc, DateTimeKind.Utc)).ToArray()
            });
            command.Parameters.AddWithValue("seconds", unique.Select(i => i.Seconds).ToArray());
            command.Parameters.AddWithValue("apps", unique.Select(i => i.App).ToArray());
            command.Parameters.AddWithValue("titles", unique.Select(i => i.Title).ToArray());
            await command.ExecuteNonQueryAsync(token);
        }

        // Повідомлення користувача; тригер у базі одразу кличе функцію асистента
        public async Task<long> InsertUserMessageAsync(Guid deviceId, string text, CancellationToken token = default)
        {
            await using var connection = await OpenAsync(token);
            await using var command = new NpgsqlCommand("""
                insert into public.ft_assistant_messages (role, kind, text, device_id, status)
                values ('user', 'chat', @text, @device, 'pending')
                returning id;
                """, connection);
            command.Parameters.AddWithValue("text", text);
            command.Parameters.AddWithValue("device", deviceId);
            return (long)(await command.ExecuteScalarAsync(token))!;
        }

        public async Task<CloudAssistantMessage?> GetMessageAsync(long id, CancellationToken token = default) =>
            (await QueryMessagesAsync("where id = @id", command => command.Parameters.AddWithValue("id", id), token))
                .FirstOrDefault();

        // Нові повідомлення після afterId, від старших до новіших
        public Task<List<CloudAssistantMessage>> GetMessagesAfterAsync(long afterId, CancellationToken token = default) =>
            QueryMessagesAsync("where id > @after order by id limit 200",
                command => command.Parameters.AddWithValue("after", afterId), token);

        // Останні count повідомлень, від старших до новіших
        public async Task<List<CloudAssistantMessage>> GetRecentMessagesAsync(int count, CancellationToken token = default)
        {
            var messages = await QueryMessagesAsync("order by id desc limit @count",
                command => command.Parameters.AddWithValue("count", count), token);
            messages.Reverse();
            return messages;
        }

        public async Task<long> GetLastMessageIdAsync(CancellationToken token = default)
        {
            await using var connection = await OpenAsync(token);
            await using var command = new NpgsqlCommand(
                "select coalesce(max(id), 0) from public.ft_assistant_messages;", connection);
            return Convert.ToInt64(await command.ExecuteScalarAsync(token));
        }

        private async Task<List<CloudAssistantMessage>> QueryMessagesAsync(string condition, Action<NpgsqlCommand> parameters,
            CancellationToken token)
        {
            await using var connection = await OpenAsync(token);
            await using var command = new NpgsqlCommand($"""
                select id, created_at, role, kind, text, device_id, target_device_id, status, error
                from public.ft_assistant_messages
                {condition};
                """, connection);
            parameters(command);

            var messages = new List<CloudAssistantMessage>();
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                messages.Add(new CloudAssistantMessage(
                    reader.GetInt64(0),
                    reader.GetDateTime(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetGuid(5),
                    reader.IsDBNull(6) ? null : reader.GetGuid(6),
                    reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8)));
            }
            return messages;
        }

        public async Task<CloudFocus?> GetFocusAsync(CancellationToken token = default)
        {
            await using var connection = await OpenAsync(token);
            await using var command = new NpgsqlCommand(
                "select until, reason, apps, updated_at from public.ft_focus where id = 1;", connection);
            await using var reader = await command.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) return null;
            return new CloudFocus(
                reader.IsDBNull(0) ? null : reader.GetDateTime(0),
                reader.GetString(1),
                reader.GetFieldValue<string[]>(2),
                reader.GetDateTime(3));
        }

        public async Task ClearFocusAsync(CancellationToken token = default)
        {
            await using var connection = await OpenAsync(token);
            await using var command = new NpgsqlCommand(
                "update public.ft_focus set until = null, apps = '{}', updated_at = now() where id = 1;", connection);
            await command.ExecuteNonQueryAsync(token);
        }

        public async Task<CloudAssistantState?> GetAssistantStateAsync(CancellationToken token = default)
        {
            await using var connection = await OpenAsync(token);
            await using var command = new NpgsqlCommand(
                "select last_run_at, last_check_at, last_error from public.ft_assistant_state where id = 1;", connection);
            await using var reader = await command.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) return null;
            return new CloudAssistantState(
                reader.IsDBNull(0) ? null : reader.GetDateTime(0),
                reader.IsDBNull(1) ? null : reader.GetDateTime(1),
                reader.IsDBNull(2) ? null : reader.GetString(2));
        }

        // Розклад і тригер, що будять функцію асистента: pg_cron раз на 5 хвилин ({"reason":"tick"})
        // і тригер на нові повідомлення користувача ({"reason":"chat"}). Секрет для заголовка
        // x-ft-secret читається з ft_assistant_state під час виклику, тож у тексті завдань його немає.
        public async Task SetupAssistantAsync(string functionUrl, CancellationToken token = default)
        {
            if (!Uri.TryCreate(functionUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                throw new ArgumentException("Адреса функції має бути посиланням https://", nameof(functionUrl));

            await EnsureSchemaAsync(token);

            await using var connection = await OpenAsync(token);
            await using var command = new NpgsqlCommand($$"""
                create extension if not exists pg_cron;
                create extension if not exists pg_net with schema extensions;

                create or replace function public.ft_assistant_chat_notify() returns trigger
                language plpgsql security definer set search_path = public, extensions as $fn$
                begin
                    perform {{HttpPostSql(uri, "chat")}};
                    return null;
                end;
                $fn$;
                revoke all on function public.ft_assistant_chat_notify() from public, anon, authenticated;

                drop trigger if exists ft_assistant_chat on public.ft_assistant_messages;
                create trigger ft_assistant_chat after insert on public.ft_assistant_messages
                    for each row when (new.role = 'user') execute function public.ft_assistant_chat_notify();

                select cron.unschedule(jobid) from cron.job where jobname = '{{CronJobName}}';
                select cron.schedule('{{CronJobName}}', '*/5 * * * *', $cmd$ select {{HttpPostSql(uri, "tick")}}; $cmd$);
                """, connection);
            await command.ExecuteNonQueryAsync(token);
        }

        private static string HttpPostSql(Uri uri, string reason) => $$"""
            net.http_post(
                url := '{{uri.AbsoluteUri.Replace("'", "''")}}',
                body := jsonb_build_object('reason', '{{reason}}'),
                headers := jsonb_build_object(
                    'Content-Type', 'application/json',
                    'x-ft-secret', (select cron_secret::text from public.ft_assistant_state where id = 1)),
                timeout_milliseconds := 120000)
            """;
    }
}
