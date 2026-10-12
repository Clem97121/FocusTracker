using Xunit;
using FocusTracker.Data;
using Microsoft.Data.Sqlite;

namespace FocusTracker.Tests;

public sealed class DatabaseBackupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"focustracker-backup-{Guid.NewGuid():N}");
    private readonly string _dbPath;
    private readonly string _backupDir;

    public DatabaseBackupTests()
    {
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "focus_tracker.db");
        _backupDir = Path.Combine(_dir, "backups");

        // База у WAL з даними, які ще не перенесені в основний файл
        using var connection = new SqliteConnection($"Data Source={_dbPath};Pooling=False");
        connection.Open();
        Execute(connection, "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0;");
        Execute(connection, "CREATE TABLE Items (Name TEXT); INSERT INTO Items VALUES ('a'), ('b');");
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    [Fact]
    public void Creates_backup_with_all_data()
    {
        var path = DatabaseBackup.CreateDailyBackup(_dbPath, _backupDir, new DateTime(2026, 10, 12, 9, 0, 0));

        Assert.Equal(Path.Combine(_backupDir, "focus_tracker-20261012.db"), path);
        using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Items";
        Assert.Equal(2L, command.ExecuteScalar());
    }

    [Fact]
    public void Second_call_on_same_day_does_not_create_new_backup()
    {
        Assert.NotNull(DatabaseBackup.CreateDailyBackup(_dbPath, _backupDir, new DateTime(2026, 10, 12, 9, 0, 0)));
        Assert.Null(DatabaseBackup.CreateDailyBackup(_dbPath, _backupDir, new DateTime(2026, 10, 12, 18, 0, 0)));
        Assert.Single(Directory.GetFiles(_backupDir));
    }

    [Fact]
    public void Keeps_only_latest_backups()
    {
        var start = new DateTime(2026, 10, 1);
        for (int day = 0; day < DatabaseBackup.KeepCount + 3; day++)
            DatabaseBackup.CreateDailyBackup(_dbPath, _backupDir, start.AddDays(day));

        var files = Directory.GetFiles(_backupDir).Select(Path.GetFileName).OrderBy(n => n).ToList();
        Assert.Equal(DatabaseBackup.KeepCount, files.Count);
        Assert.Equal("focus_tracker-20261004.db", files.First());
        Assert.Equal("focus_tracker-20261017.db", files.Last());
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }
}
