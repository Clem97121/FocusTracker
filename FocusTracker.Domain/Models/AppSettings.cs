namespace FocusTracker.Domain.Models;

public class AppSettings
{
    public const int DefaultIdleThresholdMinutes = 3;
    public const int MinIdleThresholdMinutes = 1;
    public const int MaxIdleThresholdMinutes = 60;

    // Скільки хвилин без введення, звуку чи повноекранного вікна до того, як час перестає рахуватись
    public int IdleThresholdMinutes { get; set; } = DefaultIdleThresholdMinutes;

    // Ідентифікатор цього пристрою в хмарній базі; створюється один раз
    public Guid DeviceId { get; set; }

    // Назва пристрою, яку бачать інші пристрої (за замовчуванням — ім'я комп'ютера)
    public string DeviceName { get; set; } = string.Empty;

    public bool CloudSyncEnabled { get; set; }

    // Рядок підключення до хмарної бази, зашифрований DPAPI для поточного користувача Windows
    public string? CloudConnectionProtected { get; set; }

    // === Джарвіс: асистент продуктивності на базі Claude
    public const string DefaultJarvisModel = "claude-opus-5-5";
    public const int DefaultJarvisCheckMinutes = 20;

    public bool JarvisEnabled { get; set; }

    // Ключ Anthropic API, зашифрований DPAPI для поточного користувача Windows
    public string? AnthropicKeyProtected { get; set; }

    public string JarvisModel { get; set; } = DefaultJarvisModel;

    // Цілі та пріоритети своїми словами — Джарвіс оцінює поведінку відносно них
    public string Goals { get; set; } = string.Empty;

    // Додаткові правила: коли нагадувати, що вважати відпочинком тощо
    public string JarvisRules { get; set; } = string.Empty;

    // Продуктивні години (за місцевим часом); перевірки йдуть лише в цей проміжок
    public int ProductiveFromHour { get; set; } = 12;
    public int ProductiveToHour { get; set; } = 18;

    public int JarvisCheckMinutes { get; set; } = DefaultJarvisCheckMinutes;

    // Чи може Джарвіс сам вмикати фокус-режим, що закриває відволікаючі програми
    public bool JarvisMayStartFocus { get; set; } = true;

    // «Мозок у хмарі»: перевірки й чат виконує функція Supabase, а не цей застосунок.
    // Працює, лише коли налаштована синхронізація з хмарою.
    public bool AssistantInCloud { get; set; }

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    public AppSettings Normalized()
    {
        var copy = Clone();
        copy.IdleThresholdMinutes = Math.Clamp(IdleThresholdMinutes, MinIdleThresholdMinutes, MaxIdleThresholdMinutes);
        copy.ProductiveFromHour = Math.Clamp(ProductiveFromHour, 0, 23);
        copy.ProductiveToHour = Math.Clamp(ProductiveToHour, 1, 24);
        copy.JarvisCheckMinutes = Math.Clamp(JarvisCheckMinutes, 5, 120);
        if (string.IsNullOrWhiteSpace(copy.JarvisModel))
            copy.JarvisModel = DefaultJarvisModel;
        if (copy.DeviceId == Guid.Empty)
            copy.DeviceId = Guid.NewGuid();
        if (string.IsNullOrWhiteSpace(copy.DeviceName))
            copy.DeviceName = Environment.MachineName;
        copy.DeviceName = copy.DeviceName.Trim();
        return copy;
    }
}
