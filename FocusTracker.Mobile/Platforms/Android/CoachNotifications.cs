using Android.App;
using Android.Content;
using Android.OS;
using FocusTracker.Mobile.Services;

namespace FocusTracker.Mobile.Platforms.Android;

// Повідомлення Коуча як системні сповіщення телефону
public static class CoachNotifications
{
    private const string ChannelId = "coach";
    private const string LastSeenKey = "coach.last_seen_id";
    // Старі втручання (поки телефон був без мережі) вже неактуальні
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(30);

    public const string OpenChatExtra = "open_chat";

    // Нові втручання Коуча для цього телефону (або для всіх пристроїв) → сповіщення
    public static async Task CheckAsync(Context context, Guid deviceId)
    {
        var client = new CoachClient();
        if (!long.TryParse(Preferences.Get(LastSeenKey, ""), out var lastSeen))
        {
            // Уперше не показуємо історію, лише нове
            var recent = await client.RecentAsync(1);
            Preferences.Set(LastSeenKey, (recent.LastOrDefault()?.Id ?? 0).ToString());
            return;
        }

        var messages = await client.AfterAsync(lastSeen);
        if (messages.Count == 0) return;
        Preferences.Set(LastSeenKey, messages[^1].Id.ToString());

        foreach (var message in messages.Where(m => m.Role == "assistant" && m.Kind != "chat"
                     && (m.TargetDeviceId == null || m.TargetDeviceId == deviceId)
                     && DateTime.UtcNow - m.CreatedAtUtc < MaxAge))
        {
            ShowOnce(context, message);
        }
    }

    // Те саме повідомлення може прийти і push, і під час синхронізації — показуємо один раз
    public static void ShowOnce(Context context, CoachMessage message)
    {
        const string shownKey = "coach.shown_ids";
        var shown = Preferences.Get(shownKey, "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (shown.Contains(message.Id.ToString())) return;

        shown.Add(message.Id.ToString());
        Preferences.Set(shownKey, string.Join(",", shown.TakeLast(50)));
        Show(context, message);
    }

    public static void Show(Context context, CoachMessage message)
    {
        var manager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
        if (manager.GetNotificationChannel(ChannelId) == null)
        {
            manager.CreateNotificationChannel(new NotificationChannel(ChannelId, "Коуч", NotificationImportance.High)
            {
                Description = "Нагадування, плани й фокус-режим від Коуча",
            });
        }

        var intent = new Intent(context, typeof(MainActivity));
        intent.PutExtra(OpenChatExtra, true);
        intent.AddFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        var pending = PendingIntent.GetActivity(context, 0, intent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        var title = message.Kind switch
        {
            "focus" => "Коуч · фокус-режим",
            "plan" => "Коуч · план",
            "review" => "Коуч · підсумок дня",
            _ => "Коуч",
        };
        var notification = new Notification.Builder(context, ChannelId)
            .SetSmallIcon(Resource.Mipmap.appicon)
            .SetContentTitle(title)
            .SetContentText(message.Text)
            .SetStyle(new Notification.BigTextStyle().BigText(message.Text))
            .SetContentIntent(pending)
            .SetAutoCancel(true)
            .Build();
        manager.Notify((int)(message.Id % int.MaxValue), notification);
    }

    // Android 13+: дозвіл на сповіщення запитується явно
    public static void RequestPermission(Activity activity)
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu
            && activity.CheckSelfPermission(global::Android.Manifest.Permission.PostNotifications) != global::Android.Content.PM.Permission.Granted)
        {
            activity.RequestPermissions(new[] { global::Android.Manifest.Permission.PostNotifications }, 1);
        }
    }
}
