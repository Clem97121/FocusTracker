using System;
using System.IO;

namespace FocusTracker.App.Helpers
{
    // Простий лог у файл: %LOCALAPPDATA%\FocusTracker\logs\app-yyyyMMdd.log
    public static class AppLog
    {
        private static readonly object _lock = new();

        public static string LogDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FocusTracker",
            "logs");

        public static void Info(string message) => Write("INFO", message);

        public static void Error(string message, Exception? ex = null) =>
            Write("ERROR", ex == null ? message : $"{message}{Environment.NewLine}{ex}");

        private static void Write(string level, string message)
        {
            try
            {
                lock (_lock)
                {
                    Directory.CreateDirectory(LogDirectory);
                    var file = Path.Combine(LogDirectory, $"app-{DateTime.Now:yyyyMMdd}.log");
                    File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // Лог не повинен валити застосунок
            }
        }
    }
}
