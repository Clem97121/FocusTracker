using Npgsql;

namespace FocusTracker.Data.Cloud
{
    // Ref проєкту Supabase з рядка підключення: у Session pooler користувач — "postgres.<ref>",
    // у прямому підключенні хост — "db.<ref>.supabase.co"
    public static class SupabaseProject
    {
        private const string DirectHostSuffix = ".supabase.co";

        public static string? GetRef(string? connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) return null;

            var (user, host) = ParseUserAndHost(connectionString.Trim());

            var dot = user?.IndexOf('.') ?? -1;
            if (dot > 0 && IsRef(user![(dot + 1)..]))
                return user[(dot + 1)..].ToLowerInvariant();

            if (host != null && host.StartsWith("db.", StringComparison.OrdinalIgnoreCase)
                && host.EndsWith(DirectHostSuffix, StringComparison.OrdinalIgnoreCase))
            {
                var candidate = host[3..^DirectHostSuffix.Length];
                if (IsRef(candidate)) return candidate.ToLowerInvariant();
            }
            return null;
        }

        // Адреса Edge Function проєкту, напр. https://<ref>.supabase.co/functions/v1/assistant
        public static string? GetFunctionUrl(string? connectionString, string function = "assistant") =>
            GetRef(connectionString) is { } projectRef ? $"https://{projectRef}.supabase.co/functions/v1/{function}" : null;

        // Розуміє і формат .NET (Host=…;Username=…), і URI postgresql://user:pass@host:port/db
        private static (string? User, string? Host) ParseUserAndHost(string connectionString)
        {
            if (connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
                || connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
            {
                if (!Uri.TryCreate(connectionString, UriKind.Absolute, out var uri)) return (null, null);
                var user = uri.UserInfo.Split(':')[0];
                return (Uri.UnescapeDataString(user), uri.Host);
            }

            try
            {
                var builder = new NpgsqlConnectionStringBuilder(connectionString);
                return (builder.Username, builder.Host);
            }
            catch (ArgumentException)
            {
                return (null, null);
            }
        }

        private static bool IsRef(string value) =>
            value.Length is >= 6 and <= 40 && value.All(char.IsAsciiLetterOrDigit);
    }
}
