using Android.AccessibilityServices;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Views;
using Android.Views.Accessibility;
using Android.Widget;
using FocusTracker.Mobile.Services;

namespace FocusTracker.Mobile.Platforms.Android;

// Фокус-режим на телефоні: коли відкривається заблокована програма, повертаємо на робочий стіл
// і коротко пояснюємо чому. Служба спеціальних можливостей дізнається про зміну вікна одразу,
// без постійного опитування й без значка в шторці.
[Service(Permission = "android.permission.BIND_ACCESSIBILITY_SERVICE", Exported = true, Label = "FocusTracker: фокус-режим")]
[IntentFilter(new[] { "android.accessibilityservice.AccessibilityService" })]
[MetaData("android.accessibilityservice", Resource = "@xml/focus_blocker")]
public class FocusBlockerService : AccessibilityService
{
    public override void OnAccessibilityEvent(AccessibilityEvent? e)
    {
        if (e?.EventType != EventTypes.WindowStateChanged || !FocusState.IsActive) return;
        var package = e.PackageName?.ToString();
        if (string.IsNullOrEmpty(package) || package == PackageName) return;

        var label = Label(package);
        if (!FocusState.Blocks(label, package)) return;

        PerformGlobalAction(GlobalAction.Home);
        var until = FocusState.UntilUtc?.ToLocalTime();
        var text = until is { } u ? $"{label}: фокус-режим до {u:HH:mm}" : $"{label}: фокус-режим";
        Toast.MakeText(this, text, ToastLength.Short)?.Show();
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
        return manager.GetEnabledAccessibilityServiceList(global::Android.AccessibilityServices.FeedbackFlags.AllMask)?
            .Any(s => s.ResolveInfo?.ServiceInfo?.PackageName == context.PackageName) == true;
    }

    public static void OpenSettings(Context context)
    {
        var intent = new Intent(global::Android.Provider.Settings.ActionAccessibilitySettings);
        intent.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(intent);
    }
}
