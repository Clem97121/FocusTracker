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
            var (loaded, complete) = Load(path);
            _current = loaded;

            // Ідентифікатор пристрою має бути сталим, тож одразу зберігаємо згенерований
            if (!complete)
                Write(loaded);
        }

        public AppSettings Current
        {
            get { lock (_lock) return _current; }
        }

        public event Action<AppSettings>? Changed;

        public void Save(AppSettings settings)
        {
            var normalized = settings.Normalized();
            Write(normalized);

            lock (_lock) _current = normalized;
            Changed?.Invoke(normalized);
        }

        // Змінює лише потрібні поля, решту бере з поточних налаштувань
        public void Update(Action<AppSettings> change)
        {
            var copy = Current.Clone();
            change(copy);
            Save(copy);
        }

        private void Write(AppSettings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tempPath = _path + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(tempPath, _path, overwrite: true);
        }

        // Відсутній або пошкоджений файл — значення за замовчуванням, а не падіння.
        // complete = false, якщо файлу не було або в ньому бракувало ідентифікатора пристрою.
        private static (AppSettings Settings, bool Complete) Load(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
                    return (loaded.Normalized(), loaded.DeviceId != Guid.Empty);
                }
            }
            catch (JsonException)
            {
            }

            return (new AppSettings().Normalized(), false);
        }
    }
}
