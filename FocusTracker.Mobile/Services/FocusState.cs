using System.Text.Json;

namespace FocusTracker.Mobile.Services;

// Спільний фокус-режим (ft_focus) на телефоні: до якого часу й які програми блокуються.
// Зберігається локально, щоб служба блокування працювала й без мережі.
public static class FocusState
{
    public static DateTime? UntilUtc
    {
        get
        {
            var ticks = Preferences.Get("focus.until", 0L);
            return ticks > 0 ? new DateTime(ticks, DateTimeKind.Utc) : null;
        }
    }

    public static string Reason => Preferences.Get("focus.reason", "");

    public static IReadOnlyList<string> Apps =>
        Preferences.Get("focus.apps", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);

    public static bool IsActive => UntilUtc is { } until && until > DateTime.UtcNow;

    // Програма заблокована, якщо її назва або пакет збігається з якимось записом списку
    public static bool Blocks(string label, string package)
    {
        if (!IsActive) return false;
        return Apps.Any(app =>
            app.Equals(label, StringComparison.OrdinalIgnoreCase)
            || app.Equals(package, StringComparison.OrdinalIgnoreCase)
            || package.EndsWith("." + app, StringComparison.OrdinalIgnoreCase));
    }

    public static void Set(DateTime? untilUtc, string reason, IEnumerable<string> apps)
    {
        Preferences.Set("focus.until", untilUtc?.Ticks ?? 0L);
        Preferences.Set("focus.reason", reason);
        Preferences.Set("focus.apps", string.Join("\n", apps));
    }

    // Стан із хмари: викликається під час синхронізації та одразу після push про фокус-режим
    public static async Task RefreshAsync(PhoneAccount account)
    {
        var token = await account.GetAccessTokenAsync();
        using var rows = await account.Api().SelectAsync("ft_focus", "select=until,reason,apps&id=eq.1", token);
        var row = rows.RootElement.EnumerateArray().FirstOrDefault();
        if (row.ValueKind != JsonValueKind.Object) return;

        DateTime? until = row.GetProperty("until").ValueKind == JsonValueKind.String
            ? row.GetProperty("until").GetDateTime().ToUniversalTime()
            : null;
        var apps = row.GetProperty("apps").EnumerateArray().Select(a => a.GetString() ?? "").Where(a => a.Length > 0);
        Set(until, row.GetProperty("reason").GetString() ?? "", apps);
    }
}
