using System.Text.Json;
using FocusTracker.Domain;
using FocusTracker.Domain.Models;

namespace FocusTracker.Data.Cloud
{
    // Налаштування асистента, які читає хмарна функція (рядок id = 1 у ft_assistant_settings).
    // Назви полів — ті самі, що в supabase/functions/assistant/index.ts.
    public static class AssistantCloudSettings
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        public static string ToJson(AppSettings s, TimeZoneInfo timeZone) => JsonSerializer.Serialize(new
        {
            goals = s.Goals,
            rules = s.JarvisRules,
            fromHour = s.ProductiveFromHour,
            toHour = s.ProductiveToHour,
            checkMinutes = s.JarvisCheckMinutes,
            mayStartFocus = s.JarvisMayStartFocus,
            model = s.JarvisModel,
            // Асистент живе лише в хмарі; вимкнений тут — вимкнений і там
            enabled = s.JarvisEnabled,
            timeZone = IanaId(timeZone),
            name = AssistantInfo.Name,
        }, Json);

        // Функція працює з IANA-зонами (Europe/Kyiv), а Windows дає свої (FLE Standard Time)
        public static string IanaId(TimeZoneInfo timeZone)
        {
            if (timeZone.HasIanaId) return timeZone.Id;
            return TimeZoneInfo.TryConvertWindowsIdToIanaId(timeZone.Id, out var iana) ? iana : "UTC";
        }
    }
}
