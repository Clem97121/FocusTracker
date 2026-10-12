using System.Globalization;
using Microsoft.Data.Sqlite;

namespace FocusTracker.Data
{
    public static class DatabaseBackup
    {
        public const int KeepCount = 14;

        private const string FilePrefix = "focus_tracker-";
        private const string DateFormat = "yyyyMMdd";

        // Раз на добу копіює базу в backupDir (focus_tracker-yyyyMMdd.db) і лишає останні keepCount копій.
        // Повертає шлях нової копії або null, якщо сьогоднішня вже є.
        public static string? CreateDailyBackup(string dbPath, string backupDir, DateTime now, int keepCount = KeepCount)
        {
            if (!File.Exists(dbPath))
                return null;

            Directory.CreateDirectory(backupDir);
            var backupPath = Path.Combine(backupDir, $"{FilePrefix}{now.ToString(DateFormat, CultureInfo.InvariantCulture)}.db");
            if (File.Exists(backupPath))
                return null;

            // Backup API коректно забирає й те, що ще лежить у -wal; копіюємо через тимчасовий файл,
            // щоб обірвана копія не виглядала як готова
            var tempPath = backupPath + ".tmp";
            using (var source = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Pooling=False"))
            using (var destination = new SqliteConnection($"Data Source={tempPath};Pooling=False"))
            {
                source.Open();
                destination.Open();
                source.BackupDatabase(destination);
            }
            File.Move(tempPath, backupPath, overwrite: true);

            RemoveOldBackups(backupDir, keepCount);
            return backupPath;
        }

        private static void RemoveOldBackups(string backupDir, int keepCount)
        {
            var backups = Directory.GetFiles(backupDir, $"{FilePrefix}*.db")
                .Where(path => DateTime.TryParseExact(
                    Path.GetFileNameWithoutExtension(path).Substring(FilePrefix.Length),
                    DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                .OrderByDescending(path => Path.GetFileName(path), StringComparer.Ordinal)
                .Skip(keepCount);

            foreach (var path in backups)
                File.Delete(path);
        }
    }
}
