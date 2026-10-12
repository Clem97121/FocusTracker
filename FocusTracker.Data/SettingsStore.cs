using System.Text.Json;
using FocusTracker.Domain.Models;

namespace FocusTracker.Data
{
    // Налаштування застосунку в settings.json поруч із базою
    public class SettingsStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private readonly string _path;
        private readonly object _lock = new();
        private AppSettings _current;

        public SettingsStore(string path)
        {
            _path = path;
            _current = Load(path);
        }

        public AppSettings Current
        {
            get { lock (_lock) return _current; }
        }

        public event Action<AppSettings>? Changed;

        public void Save(AppSettings settings)
        {
            var normalized = settings.Normalized();

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tempPath = _path + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(normalized, JsonOptions));
            File.Move(tempPath, _path, overwrite: true);

            lock (_lock) _current = normalized;
            Changed?.Invoke(normalized);
        }

        // Відсутній або пошкоджений файл — значення за замовчуванням, а не падіння
        private static AppSettings Load(string path)
        {
            try
            {
                if (File.Exists(path))
                    return (JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings()).Normalized();
            }
            catch (JsonException)
            {
            }

            return new AppSettings();
        }
    }
}
