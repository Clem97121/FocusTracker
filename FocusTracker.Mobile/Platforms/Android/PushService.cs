using Android.App;
using Android.Gms.Extensions;
using Firebase.Messaging;
using FocusTracker.Mobile.Services;

namespace FocusTracker.Mobile.Platforms.Android;

// Push від хмарної функції Коуча (Firebase Cloud Messaging): одразу показуємо сповіщення
[Service(Exported = false)]
[IntentFilter(new[] { "com.google.firebase.MESSAGING_EVENT" })]
public class PushService : FirebaseMessagingService
{
    private const string TokenKey = "push.token";
    private const string UploadedTokenKey = "push.token_uploaded";

    public override void OnMessageReceived(RemoteMessage message)
    {
        var data = message.Data;
        if (data == null || !data.TryGetValue("text", out var text)
            || !data.TryGetValue("message_id", out var idText) || !long.TryParse(idText, out var id))
            return;

        var kind = data.TryGetValue("kind", out var k) ? k : "nudge";
        var coach = new CoachMessage(id, DateTime.UtcNow, "assistant", kind, text, null, "done", null);
        CoachNotifications.ShowOnce(this, coach);

        // Фокус-режим починає діяти на телефоні одразу, а не з наступною синхронізацією
        if (kind == "focus")
        {
            try
            {
                Task.Run(() => FocusState.RefreshAsync(new PhoneAccount())).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Focus refresh: {ex.Message}");
            }
        }
    }

    public override void OnNewToken(string token)
    {
        Preferences.Set(TokenKey, token);
        // Відправимо з наступною синхронізацією (тут немає гарантії мережі й входу)
        Preferences.Remove(UploadedTokenKey);
    }

    // Токен цього телефону в ft_push_tokens, якщо він новий або ще не відправлявся
    public static async Task UploadTokenAsync(SupabaseApi api, string accessToken, Guid deviceId)
    {
        string? token;
        try
        {
            token = (await FirebaseMessaging.Instance.GetToken())?.ToString();
        }
        catch (Exception ex)
        {
            // Немає google-services.json (напр. тестова збірка) — працюємо без push
            System.Diagnostics.Debug.WriteLine($"FCM token: {ex.Message}");
            return;
        }
        if (string.IsNullOrEmpty(token) || Preferences.Get(UploadedTokenKey, "") == token) return;

        await api.UpsertAsync("ft_push_tokens", "device_id",
            new[] { new { device_id = deviceId, token, updated_at = DateTime.UtcNow } }, accessToken);
        Preferences.Set(UploadedTokenKey, token);
    }
}
