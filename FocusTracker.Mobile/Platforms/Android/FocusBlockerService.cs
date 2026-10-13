using Android.AccessibilityServices;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Views.Accessibility;
using FocusTracker.Mobile.Services;

namespace FocusTracker.Mobile.Platforms.Android;

// Фокус-режим на телефоні: коли відкривається заблокована програма, повертаємо на робочий стіл
// і показуємо заглушку Коуча. Служба спеціальних можливостей дізнається про зміну вікна одразу,
// без постійного опитування й без значка в шторці.
[Service(Permission = "android.permission.BIND_ACCESSIBILITY_SERVICE", Exported = true, Label = "FocusTracker: фокус-режим")]
[IntentFilter(new[] { "android.accessibilityservice.AccessibilityService" })]
[MetaData("android.accessibilityservice", Resource = "@xml/focus_blocker")]
public class FocusBlockerService : AccessibilityService
{
    private string? _lastBlocked;
    private DateTime _lastBlockedAt;

    public override void OnAccessibilityEvent(AccessibilityEvent? e)
    {
        if (e?.EventType != EventTypes.WindowStateChanged) return;
        var package = e.PackageName?.ToString();
        if (string.IsNullOrEmpty(package) || package == PackageName || !FocusState.IsActive) return;

        if (!FocusState.Blocks(Label(package), package)) return;

        // Одне вікно програми може дати кілька подій поспіль — не відкриваємо заглушку двічі
        if (package == _lastBlocked && DateTime.UtcNow - _lastBlockedAt < TimeSpan.FromSeconds(2)) return;
        _lastBlocked = package;
        _lastBlockedAt = DateTime.UtcNow;

        PerformGlobalAction(GlobalAction.Home);
        var intent = new Intent(this, typeof(FocusBlockActivity));
        intent.PutExtra(FocusBlockActivity.AppExtra, Label(package));
        intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
        StartActivity(intent);
    }

    public override void OnInterrupt()
    {
    }

    private string Label(string package)
    {
        try
        {
            var info = PackageManager!.GetApplicationInfo(package, PackageInfoFlags.MetaData);
            return PackageManager.GetApplicationLabel(info) ?? package;
        }
        catch (PackageManager.NameNotFoundException)
        {
            return package;
        }
    }

    public static bool IsEnabled(Context context)
    {
        var manager = (AccessibilityManager)context.GetSystemService(AccessibilityService)!;
        return manager.GetEnabledAccessibilityServiceList(FeedbackFlags.AllMask)?
            .Any(s => s.ResolveInfo?.ServiceInfo?.PackageName == context.PackageName) == true;
    }

    public static void OpenSettings(Context context)
    {
        var intent = new Intent(global::Android.Provider.Settings.ActionAccessibilitySettings);
        intent.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(intent);
    }
}
