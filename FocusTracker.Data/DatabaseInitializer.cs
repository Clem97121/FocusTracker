using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FocusTracker.Data
{
    public static class DatabaseInitializer
    {
        // Перша міграція поточної історії. Бази, створені до неї (dev-версії до 26.05.2025),
        // мають інші таблиці, тож Migrate() на них падає з "table already exists".
        private const string BaselineMigration = "20250526150340_InitClean";

        // Застосовує міграції. Якщо база стара й несумісна, переносить її в архів поруч
        // (focus_tracker.legacy-yyyyMMdd-HHmmss.db) і створює нову. Повертає шлях архіву або null.
        public static string? Initialize(IDbContextFactory<FocusTrackerDbContext> factory, string dbPath)
        {
            string? archivedPath = null;

            if (File.Exists(dbPath) && IsLegacyDatabase(dbPath))
                archivedPath = ArchiveDatabase(dbPath);

            using var db = factory.CreateDbContext();
            db.Database.Migrate();
            // WAL дозволяє читати БД з UI, поки трекер пише
            db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");

            return archivedPath;
        }

        public static bool IsLegacyDatabase(string dbPath)
        {
            using var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False");
            connection.Open();

            bool hasAppTables = TableExists(connection, "AppUsageStats");
            if (!hasAppTables)
                return false;

            if (!TableExists(connection, "__EFMigrationsHistory"))
                return true;

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = $id";
            command.Parameters.AddWithValue("$id", BaselineMigration);
            return Convert.ToInt64(command.ExecuteScalar()) == 0;
        }

        private static bool TableExists(SqliteConnection connection, string table)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
            command.Parameters.AddWithValue("$name", table);
            return Convert.ToInt64(command.ExecuteScalar()) > 0;
        }

        private static string ArchiveDatabase(string dbPath)
        {
            SqliteConnection.ClearAllPools();

            var directory = Path.GetDirectoryName(dbPath)!;
            var name = Path.GetFileNameWithoutExtension(dbPath);
            var archivedPath = Path.Combine(directory, $"{name}.legacy-{DateTime.Now:yyyyMMdd-HHmmss}.db");

            // -wal і -shm переносимо разом із базою, інакше архів буде неповним
            File.Move(dbPath, archivedPath);
            foreach (var suffix in new[] { "-wal", "-shm" })
            {
                if (File.Exists(dbPath + suffix))
                    File.Move(dbPath + suffix, archivedPath + suffix);
            }

            return archivedPath;
        }
    }
}
