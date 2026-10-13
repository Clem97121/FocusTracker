using Npgsql;
using NpgsqlTypes;

namespace FocusTracker.Data.Cloud
{
    public record CloudHourlyRow(Guid DeviceId, string AppName, DateOnly Date, int Hour,
        double TotalSeconds, double ActiveSeconds, DateTime UpdatedAtUtc);

    public record CloudDevice(Guid DeviceId, string Name, DateTime LastSeenAtUtc);

    // Категорія, навичка, задача, обмеження або запис XP; Data — JSON, null для видалених
    public record CloudEntity(string Kind, Guid SyncId, string? Data, bool Deleted, DateTime UpdatedAtUtc, Guid UpdatedBy);

    // Налаштування програми зі сторінки «Застосунки»; UpdatedAtUtc — коли їх змінив користувач
    public record CloudProgramSettings(string Identifier, string Category, bool IsTracked, bool IsHidden,
        bool IgnoreIdle, DateTime UpdatedAtUtc, Guid UpdatedBy);

    // Доступ до хмарної бази Postgres (Supabase). Таблиці з префіксом ft_ створюються самі.
    public partial class CloudStore
    {
        // Row Level Security увімкнено: таблиці закриті для публічного REST API,
        // а пряме підключення власником бази на нього не зважає
        private const string SchemaSql = """
            create table if not exists public.ft_devices (
                device_id uuid primary key,
                name text not null,
                last_seen_at timestamptz not null default now()
            );
            create table if not exists public.ft_hourly_usage (
                device_id uuid not null references public.ft_devices(device_id) on delete cascade,
                app_name text not null,
                date date not null,
                hour integer not null check (hour between 0 and 23),
                total_seconds double precision not null,
                active_seconds double precision not null,
                updated_at timestamptz not null default now(),
                primary key (device_id, app_name, date, hour)
            );
            create index if not exists ft_hourly_usage_updated_at on public.ft_hourly_usage (updated_at);
            create table if not exists public.ft_programs (
                identifier text primary key,
                category text not null,
                is_tracked boolean not null,
                is_hidden boolean not null,
                ignore_idle boolean not null,
                updated_at timestamptz not null,
                updated_by uuid not null,
                synced_at timestamptz not null default now()
            );
            create index if not exists ft_programs_synced_at on public.ft_programs (synced_at);
            create table if not exists public.ft_program_icons (
                identifier text primary key,
                icon bytea not null,
                updated_at timestamptz not null default now()
            );
            alter table public.ft_program_icons enable row level security;
            create table if not exists public.ft_entities (
                kind text not null,
                sync_id uuid not null,
                data jsonb,
                deleted boolean not null default false,
                updated_at timestamptz not null,
                updated_by uuid not null,
                synced_at timestamptz not null default now(),
                primary key (kind, sync_id)
            );
            create index if not exists ft_entities_synced_at on public.ft_entities (synced_at);
            alter table public.ft_entities enable row level security;
            create table if not exists public.ft_activity (
                device_id uuid not null references public.ft_devices(device_id) on delete cascade,
                date date not null,
                minutes bytea not null,
                updated_at timestamptz not null default now(),
                primary key (device_id, date)
            );
            create index if not exists ft_activity_updated_at on public.ft_activity (updated_at);
            alter table public.ft_activity enable row level security;
            alter table public.ft_devices enable row level security;
            alter table public.ft_hourly_usage enable row level security;
            alter table public.ft_programs enable row level security;

            -- Асистент у хмарі: налаштування, присутність, активність, чат і стан
            create table if not exists public.ft_assistant_settings (
                id integer primary key check (id = 1),
                data jsonb not null,
                updated_at timestamptz not null default now()
            );
            alter table public.ft_assistant_settings enable row level security;
            create table if not exists public.ft_presence (
                device_id uuid primary key,
                name text not null,
                last_input_at timestamptz,
                foreground_app text,
                updated_at timestamptz not null default now()
            );
            alter table public.ft_presence enable row level security;
            create table if not exists public.ft_recent_activity (
                id bigserial primary key,
                device_id uuid not null,
                at timestamptz not null,
                seconds double precision not null default 0,
                app text not null,
                title text not null,
                unique (device_id, at, app)
            );
            create index if not exists ft_recent_activity_at on public.ft_recent_activity (at);
            alter table public.ft_recent_activity enable row level security;
            create table if not exists public.ft_assistant_messages (
                id bigserial primary key,
                created_at timestamptz not null default now(),
                role text not null check (role in ('user', 'assistant')),
                kind text not null default 'chat' check (kind in ('chat', 'nudge', 'ask', 'plan', 'focus', 'review')),
                text text not null,
                device_id uuid,
                target_device_id uuid,
                status text not null default 'done' check (status in ('pending', 'done', 'error')),
                error text
            );
            create index if not exists ft_assistant_messages_pending on public.ft_assistant_messages (id) where status = 'pending';
            alter table public.ft_assistant_messages enable row level security;
            create table if not exists public.ft_focus (
                id integer primary key check (id = 1),
                until timestamptz,
                reason text not null default '',
                apps text[] not null default '{}',
                updated_at timestamptz not null default now()
            );
            insert into public.ft_focus (id) values (1) on conflict (id) do nothing;
            alter table public.ft_focus enable row level security;
            create table if not exists public.ft_assistant_state (
                id integer primary key check (id = 1),
                last_check_at timestamptz,
                plan_date date,
                review_date date,
                memory text not null default '',
                last_error text,
                last_run_at timestamptz,
                busy_until timestamptz,
                cron_secret uuid not null default gen_random_uuid()
            );
            insert into public.ft_assistant_state (id) values (1) on conflict (id) do nothing;
            alter table public.ft_assistant_state enable row level security;

            -- Токени Firebase телефонів: хмарна функція надсилає на них push із повідомленнями Коуча
            create table if not exists public.ft_push_tokens (
                device_id uuid primary key references public.ft_devices(device_id) on delete cascade,
                token text not null,
                updated_at timestamptz not null default now()
            );
            alter table public.ft_push_tokens enable row level security;

            -- Доступ для телефону: він ходить через Supabase API під обліковим записом власника,
            -- а не паролем бази. Кожна таблиця відкрита лише власнику (ft_owner), секрет cron — нікому.
            create table if not exists public.ft_owner (
                id integer primary key check (id = 1),
                user_id uuid not null,
                email text not null,
                updated_at timestamptz not null default now()
            );
            alter table public.ft_owner enable row level security;

            create or replace function public.ft_is_owner() returns boolean
            language sql stable security definer set search_path = public as $fn$
                select exists (select 1 from public.ft_owner where user_id = auth.uid());
            $fn$;
            revoke all on function public.ft_is_owner() from public, anon;
            grant execute on function public.ft_is_owner() to authenticated;

            do $do$
            declare t text;
            begin
                foreach t in array array['ft_devices', 'ft_hourly_usage', 'ft_programs', 'ft_program_icons', 'ft_entities',
                    'ft_activity', 'ft_assistant_settings', 'ft_presence', 'ft_recent_activity', 'ft_assistant_messages', 'ft_focus', 'ft_push_tokens']
                loop
                    execute format('revoke all on public.%I from anon', t);
                    execute format('grant select, insert, update, delete on public.%I to authenticated', t);
                    execute format('drop policy if exists ft_owner_all on public.%I', t);
                    execute format('create policy ft_owner_all on public.%I for all to authenticated using (public.ft_is_owner()) with check (public.ft_is_owner())', t);
                end loop;
            end
            $do$;
            grant usage, select on sequence public.ft_recent_activity_id_seq, public.ft_assistant_messages_id_seq to authenticated;

            revoke all on public.ft_owner from anon, authenticated;
            grant select on public.ft_owner to authenticated;
            drop policy if exists ft_owner_self on public.ft_owner;
            create policy ft_owner_self on public.ft_owner for select to authenticated using (user_id = auth.uid());

            -- Стан функції без cron_secret: ним функцію будить лише сама база
            revoke all on public.ft_assistant_state from anon, authenticated;
            grant select (id, last_check_at, last_run_at, last_error, plan_date, review_date, memory) on public.ft_assistant_state to authenticated;
            drop policy if exists ft_owner_read on public.ft_assistant_state;
            create policy ft_owner_read on public.ft_assistant_state for select to authenticated using (public.ft_is_owner());
            """;

        // Змінюється, коли SchemaSql отримує нові таблиці: пристрої перевіряють схему заново
        public const int SchemaVersion = 7;

        private readonly string _connectionString;

        public CloudStore(string connectionString)
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            // Supabase приймає лише шифровані підключення
            if (builder.SslMode is SslMode.Disable or SslMode.Allow or SslMode.Prefer)
                builder.SslMode = SslMode.Require;
            builder.Timeout = Math.Max(builder.Timeout, 15);
            _connectionString = builder.ConnectionString;
        }

        // Власник даних для телефону: обліковий запис Supabase Auth з цим email отримує доступ до таблиць.
        // Повертає false, якщо такого користувача в Supabase ще немає.
        public async Task<bool> BindOwnerAsync(string email, CancellationToken token = default)
        {
            await EnsureSchemaAsync(token);
            await using var connection = await OpenAsync(token);
            await using var command = new NpgsqlCommand("""
                insert into public.ft_owner (id, user_id, email)
                select 1, u.id, u.email from auth.users u where lower(u.email) = lower(@email)
                on conflict (id) do update set user_id = excluded.user_id, email = excluded.email, updated_at = now();
                """, connection);
            command.Parameters.AddWithValue("email", email.Trim());
            return await command.ExecuteNonQueryAsync(token) > 0;
        }

        // Email власника або null, якщо телефон ще не прив'язано
        public async Task<string?> GetOwnerEmailAsync(CancellationToken token = default)
        {
            await using var connection = await OpenAsync(token);
            await using var command = new NpgsqlCommand(
                "select email from public.ft_owner where id = 1;", connection);
            try
            {
                return await command.ExecuteScalarAsync(token) as string;
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
            {
                return null;
            }
        }

        public async Task EnsureSchemaAsync(CancellationToken token = default)
        {
            await using var connection = await OpenAsync(token);
            await using var command = new NpgsqlCommand(SchemaSql, connection);
            await command.ExecuteNonQueryAsync(token);
        }

        public async Task UpsertDeviceAsync(Guid deviceId, string name, CancellationToken token = default)
        {
            await using var connection = await OpenAsync(token);
            await using var command = new NpgsqlCommand("""
                insert into public.ft_devices (device_id, name, last_seen_at) values (@id, @name, now())
                on conflict (device_id) do update set name = excluded.name, last_seen_at = now();
                """, connection);
            command.Parameters.AddWithValue("id", deviceId);
            command.Parameters.AddWithValue("name", name);
            await command.ExecuteNonQueryAsync(token);
        }

        // Записує абсолютні значення, тож повторна відправка тих самих рядків нічого не подвоює
        public async Task UploadHourlyAsync(Guid deviceId, IReadOnlyList<CloudHourlyRow> rows, CancellationToken token = default)
        {
            if (rows.Count == 0) return;

            await using var connection = await OpenAsync(token);
            await using var command = new NpgsqlCommand("""
                insert into public.ft_hourly_usage (device_id, app_name, date, hour, total_seconds, active_seconds, updated_at)
                select @device, u.app_name, u.date, u.hour, u.total_seconds, u.active_seconds, now()
                from unnest(@apps, @dates, @hours, @totals, @actives) as u(app_name, date, hour, total_seconds, active_seconds)
                on conflict (device_id, app_name, date, hour) do update
                set total_seconds = excluded.total_seconds,
                    active_seconds = excluded.active_seconds,
                    updated_at = now();
                """, connection);
            command.Parameters.AddWithValue("device", deviceId);
            command.Parameters.AddWithValue("apps", rows.Select(r => r.AppName).ToArray());
            command.Parameters.Add(new NpgsqlParameter("dates", NpgsqlDbType.Array | NpgsqlDbType.Date) { Value = rows.Select(r => r.Date).ToArray() });
            command.Parameters.AddWithValue("hours", rows.Select(r => r.Hour).ToArray());
            command.Parameters.AddWithValue("totals", rows.Select(r => r.TotalSeconds).ToArray());
            command.Parameters.AddWithValue("actives", rows.Select(r => r.ActiveSeconds).ToArray());
            await command.ExecuteNonQueryAsync(token);
        }

        // Рядки інших пристроїв, змінені після sinceUtc (час сервера)
        public async Task<List<CloudHourlyRow>> DownloadHourlyAsync(Guid excludeDeviceId, DateTime? sinceUtc, CancellationToken token = default)
        {
            await using var connection = await OpenAsync(token);
            await using var command = new NpgsqlCommand("""
                select device_id, app_name, date, hour, total_seconds, active_seconds, updated_at
                from public.ft_hourly_usage
                where device_id <> @me and (@since::timestamptz is null or updated_at > @since)
                order by updated_at;
                """, connection);
            command.Parameters.AddWithValue("me", excludeDeviceId);
            command.Parameters.Add(new NpgsqlParameter("since", NpgsqlDbType.TimestampTz) { Value = (object?)sinceUtc ?? DBNull.Value });

            var rows = new List<CloudHourlyRow>();
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                rows.Add(new CloudHourlyRow(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetFieldValue<DateOnly>(2),
                    reader.GetInt32(3),
                    reader.GetDouble(4),
                    reader.GetDouble(5),
                    reader.GetDateTime(6)));
            }
            return rows;
        }

        // Перемагає новіша зміна: старіші налаштування не перезаписують хмарні
        public async Task UploadProgramsAsync(Guid deviceId, IReadOnlyList<CloudProgramSettings> programs, CancellationToken token = default)
        {
            if (programs.Count == 0) return;

            await using var connection = await OpenAsync(token);
            await using var command = new NpgsqlCommand("""
                insert into public.ft_programs (identifier, category, is_tracked, is_hidden, ignore_idle, updated_at, updated_by, synced_at)
                select u.identifier, u.category, u.is_tracked, u.is_hidden, u.ignore_idle, u.updated_at, @device, now()
                from unnest(@ids, @categories, @tracked, @hidden, @ignoreIdle, @updated)
                    as u(identifier, category, is_tracked, is_hidden, ignore_idle, updated_at)
                on conflict (identifier) do update
                set category = excluded.category,
                    is_tracked = excluded.is_tracked,
                    is_hidden = excluded.is_hidden,
                    ignore_idle = excluded.ignore_idle,
                    updated_at = excluded.updated_at,
                    updated_by = excluded.updated_by,
                    synced_at = now()
                where excluded.updated_at > public.ft_programs.updated_at;
                """, connection);
            command.Parameters.AddWithValue("device", deviceId);
            command.Parameters.AddWithValue("ids", programs.Select(p => p.Identifier).ToArray());
            command.Parameters.AddWithValue("categories", programs.Select(p => p.Category).ToArray());
            command.Parameters.AddWithValue("tracked", programs.Select(p => p.IsTracked).ToArray());
            command.Parameters.AddWithValue("hidden", programs.Select(p => p.IsHidden).ToArray());
            command.Parameters.AddWithValue("ignoreIdle", programs.Select(p => p.IgnoreIdle).ToArray());
            command.Parameters.Add(new NpgsqlParameter("updated", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz)
            {
                Value = programs.Select(p => DateTime.SpecifyKind(p.UpdatedAtUtc, DateTimeKind.Utc)).ToArray()
            });
            await command.ExecuteNonQueryAsync(token);
        }

        // Налаштування, які інші пристрої записали після sinceUtc (час сервера)
        public async Task<List<(CloudProgramSettings Settings, DateTime SyncedAtUtc)>> DownloadProgramsAsync(
            Guid excludeDeviceId, DateTime? sinceUtc, CancellationToken token = default)
        {
            await using var connection = await OpenAsync(token);
            await using var command = new NpgsqlCommand("""
                select identifier, category, is_tracked, is_hidden, ignore_idle, updated_at, updated_by, synced_at
                from public.ft_programs
                where updated_by <> @me and (@since::timestamptz is null or synced_at > @since)
                order by synced_at;
                """, connection);
            command.Parameters.AddWithValue("me", excludeDeviceId);
            command.Parameters.Add(new NpgsqlParameter("since", NpgsqlDbType.TimestampTz) { Value = (object?)sinceUtc ?? DBNull.Value });

            var rows = new List<(CloudProgramSettings, DateTime)>();
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                rows.Add((new CloudProgramSettings(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetBoolean(2),
                    reader.GetBoolean(3),
                    reader.GetBoolean(4),
                    reader.GetDateTime(5),
                    reader.GetGuid(6)), reader.GetDateTime(7)));
            }
            return rows;
        }

        // Перемагає новіша зміна, видалення — теж зміна
        public async Task UploadEntitiesAsync(Guid deviceId, IReadOnlyList<CloudEntity> entities, CancellationToken token = default)
        {
            if (entities.Count == 0) return;

            await using var connection = await OpenAsync(token);
            await using var command = new NpgsqlCommand("""
                insert into public.ft_entities (kind, sync_id, data, deleted, updated_at, updated_by, synced_at)
                select u.kind, u.sync_id, u.data::jsonb, u.deleted, u.updated_at, @device, now()
                from unnest(@kinds, @ids, @data, @deleted, @updated) as u(kind, sync_id, data, deleted, updated_at)
                on conflict (kind, sync_id) do update
                set data = excluded.data,
                    deleted = excluded.deleted,
                    updated_at = excluded.updated_at,
                    updated_by = excluded.updated_by,
                    synced_at = now()
                where excluded.updated_at > public.ft_entities.updated_at;
                """, connection);
            command.Parameters.AddWithValue("device", deviceId);
            command.Parameters.AddWithValue("kinds", entities.Select(e => e.Kind).ToArray());
            command.Parameters.AddWithValue("ids", entities.Select(e => e.SyncId).ToArray());
            command.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = entities.Select(e => e.Data).ToArray() });
            command.Parameters.AddWithValue("deleted", entities.Select(e => e.Deleted).ToArray());
            command.Parameters.Add(new NpgsqlParameter("updated", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz)
            {
                Value = entities.Select(e => DateTime.SpecifyKind(e.UpdatedAtUtc, DateTimeKind.Utc)).ToArray()
            });
            await command.ExecuteNonQueryAsync(token);
        }

        // Сутності, які інші пристрої записали після sinceUtc (час сервера)
        public async Task<List<(CloudEntity Entity, DateTime SyncedAtUtc)>> DownloadEntitiesAsync(
            Guid excludeDeviceId, DateTime? sinceUtc, CancellationToken token = default)
        {
            await using var connection = await OpenAsync(token);
            await using var command = new NpgsqlCommand("""
                select kind, sync_id, data::text, deleted, updated_at, updated_by, synced_at
                from public.ft_entities
                where updated_by <> @me and (@since::timestamptz is null or synced_at > @since)
                order by synced_at;
                """, connection);
            command.Parameters.AddWithValue("me", excludeDeviceId);
            command.Parameters.Add(new NpgsqlParameter("since", NpgsqlDbType.TimestampTz) { Value = (object?)sinceUtc ?? DBNull.Value });

            var rows = new List<(CloudEntity, DateTime)>();
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                rows.Add((new CloudEntity(
                    reader.GetString(0),
                    reader.GetGuid(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetBoolean(3),
                    reader.GetDateTime(4),
                    reader.GetGuid(5)), reader.GetDateTime(6)));
            }
            return rows;
        }

        // Хвилини присутності за день; бітова карта перезаписується повністю
        public async Task UploadActivityAsync(Guid deviceId, IReadOnlyList<(DateOnly Date, byte[] Minutes)> days, CancellationToken token = default)
        {
            if (days.Count == 0) return;

            await using var connection = await OpenAsync(token);
            await using var command = new NpgsqlCommand("""
                insert into public.ft_activity (device_id, date, minutes, updated_at)
                select @device, u.date, u.minutes, now() from unnest(@dates, @minutes) as u(date, minutes)
                on conflict (device_id, date) do update set minutes = excluded.minutes, updated_at = now();
                """, connection);
            command.Parameters.AddWithValue("device", deviceId);
            command.Parameters.Add(new NpgsqlParameter("dates", NpgsqlDbType.Array | NpgsqlDbType.Date) { Value = days.Select(d => d.Date).ToArray() });
            command.Parameters.Add(new NpgsqlParameter("minutes", NpgsqlDbType.Array | NpgsqlDbType.Bytea) { Value = days.Select(d => d.Minutes).ToArray() });
            await command.ExecuteNonQueryAsync(token);
        }

        public async Task<List<(Guid DeviceId, DateOnly Date, byte[] Minutes, DateTime UpdatedAtUtc)>> DownloadActivityAsync(
            Guid excludeDeviceId, DateTime? sinceUtc, CancellationToken token = default)
        {
            await using var connection = await OpenAsync(token);
            await using var command = new NpgsqlCommand("""
                select device_id, date, minutes, updated_at from public.ft_activity
                where device_id <> @me and (@since::timestamptz is null or updated_at > @since)
                order by updated_at;
                """, connection);
            command.Parameters.AddWithValue("me", excludeDeviceId);
            command.Parameters.Add(new NpgsqlParameter("since", NpgsqlDbType.TimestampTz) { Value = (object?)sinceUtc ?? DBNull.Value });

            var rows = new List<(Guid, DateOnly, byte[], DateTime)>();
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
                rows.Add((reader.GetGuid(0), reader.GetFieldValue<DateOnly>(1), reader.GetFieldValue<byte[]>(2), reader.GetDateTime(3)));
            return rows;
        }

        // Іконки беруться з exe, тож є лише на пристроях, де програма встановлена.
        // У хмарі зберігаємо по одній іконці на програму і роздаємо тим, у кого її немає.
        public async Task<HashSet<string>> GetIconIdentifiersAsync(CancellationToken token = default)
        {
            await using var connection = await OpenAsync(token);
            await using var command = new NpgsqlCommand("select identifier from public.ft_program_icons;", connection);

            var identifiers = new HashSet<string>(StringComparer.Ordinal);
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
                identifiers.Add(reader.GetString(0));
            return identifiers;
        }

        public async Task UploadIconsAsync(IReadOnlyList<(string Identifier, byte[] Icon)> icons, CancellationToken token = default)
        {
            if (icons.Count == 0) return;

            await using var connection = await OpenAsync(token);
            await using var command = new NpgsqlCommand("""
                insert into public.ft_program_icons (identifier, icon)
                select u.identifier, u.icon from unnest(@ids, @icons) as u(identifier, icon)
                on conflict (identifier) do nothing;
                """, connection);
            command.Parameters.AddWithValue("ids", icons.Select(i => i.Identifier).ToArray());
            command.Parameters.Add(new NpgsqlParameter("icons", NpgsqlDbType.Array | NpgsqlDbType.Bytea) { Value = icons.Select(i => i.Icon).ToArray() });
            await command.ExecuteNonQueryAsync(token);
        }

        public async Task<List<(string Identifier, byte[] Icon)>> DownloadIconsAsync(IReadOnlyCollection<string> identifiers, CancellationToken token = default)
        {
            var icons = new List<(string, byte[])>();
            if (identifiers.Count == 0) return icons;

            await using var connection = await OpenAsync(token);
            await using var command = new NpgsqlCommand(
                "select identifier, icon from public.ft_program_icons where identifier = any(@ids);", connection);
            command.Parameters.AddWithValue("ids", identifiers.ToArray());

            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
                icons.Add((reader.GetString(0), reader.GetFieldValue<byte[]>(1)));
            return icons;
        }

        public async Task<List<CloudDevice>> GetDevicesAsync(CancellationToken token = default)
        {
            await using var connection = await OpenAsync(token);
            await using var command = new NpgsqlCommand(
                "select device_id, name, last_seen_at from public.ft_devices;", connection);

            var devices = new List<CloudDevice>();
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
                devices.Add(new CloudDevice(reader.GetGuid(0), reader.GetString(1), reader.GetDateTime(2)));
            return devices;
        }

        private async Task<NpgsqlConnection> OpenAsync(CancellationToken token)
        {
            var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(token);
            return connection;
        }
    }
}
