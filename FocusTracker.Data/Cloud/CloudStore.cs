using Npgsql;
using NpgsqlTypes;

namespace FocusTracker.Data.Cloud
{
    public record CloudHourlyRow(Guid DeviceId, string AppName, DateOnly Date, int Hour,
        double TotalSeconds, double ActiveSeconds, DateTime UpdatedAtUtc);

    public record CloudDevice(Guid DeviceId, string Name, DateTime LastSeenAtUtc);

    // Доступ до хмарної бази Postgres (Supabase). Таблиці з префіксом ft_ створюються самі.
    public class CloudStore
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
            alter table public.ft_devices enable row level security;
            alter table public.ft_hourly_usage enable row level security;
            """;

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
