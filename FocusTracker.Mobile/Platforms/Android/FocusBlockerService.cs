using Android.AccessibilityServices;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Runtime;
using Android.Views;
using Android.Views.Accessibility;
using Android.Widget;
using FocusTracker.Mobile.Services;
using Button = Android.Widget.Button;
using Color = Android.Graphics.Color;
using View = Android.Views.View;

namespace FocusTracker.Mobile.Platforms.Android;

// Фокус-режим на телефоні: коли відкривається заблокована програма, повертаємо на робочий стіл
// і показуємо заглушку Коуча. Служба спеціальних можливостей дізнається про зміну вікна одразу,
// без постійного опитування й без значка в шторці. Заглушка — вікно самої служби
// (TYPE_ACCESSIBILITY_OVERLAY): воно завжди поверх робочого столу й не потребує окремих дозволів.
[Service(Permission = "android.permission.BIND_ACCESSIBILITY_SERVICE", Exported = true, Label = "FocusTracker: фокус-режим")]
[IntentFilter(new[] { "android.accessibilityservice.AccessibilityService" })]
[MetaData("android.accessibilityservice", Resource = "@xml/focus_blocker")]
public class FocusBlockerService : AccessibilityService
{
    private View? _overlay;

    public override void OnAccessibilityEvent(AccessibilityEvent? e)
    {
        if (e?.EventType != EventTypes.WindowStateChanged) return;
        var package = e.PackageName?.ToString();
        if (string.IsNullOrEmpty(package) || package == PackageName || !FocusState.IsActive) return;

        var label = Label(package);
        if (!FocusState.Blocks(label, package)) return;

        PerformGlobalAction(GlobalAction.Home);
        ShowOverlay(label);
    }

    public override void OnInterrupt()
    {
    }

    private void ShowOverlay(string app)
    {
        HideOverlay();

        var until = FocusState.UntilUtc?.ToLocalTime();
        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
        layout.SetGravity(GravityFlags.Center);
        layout.SetPadding(64, 64, 64, 64);
        layout.SetBackgroundColor(Color.ParseColor("#F21E1E24"));

        layout.AddView(Text("Фокус-режим", 28, Color.White));
        if (until is { } u)
            layout.AddView(Text($"до {u:HH:mm}", 18, Color.ParseColor("#9E9EA8")));
        layout.AddView(Text($"{app} зараз заблоковано.", 18, Color.White));
        if (FocusState.Reason.Length > 0)
            layout.AddView(Text(FocusState.Reason, 16, Color.ParseColor("#C8C8D0")));

        var ok = new Button(this) { Text = "Зрозуміло, повертаюсь до справ" };
        ok.Click += (_, _) => HideOverlay();
        layout.AddView(ok);

        // Свідоме рішення: зняти фокус на всіх пристроях (Коуч це побачить)
        var stop = new Button(this) { Text = "Завершити фокус-режим" };
        stop.Click += async (_, _) =>
        {
            stop.Enabled = false;
            try
            {
                await FocusState.StopAsync(new PhoneAccount());
                HideOverlay();
            }
            catch (Exception ex)
            {
                stop.Enabled = true;
                Toast.MakeText(this, $"Не вдалося: {ex.Message}", ToastLength.Long)?.Show();
            }
        };
        layout.AddView(stop);

        var parameters = new WindowManagerLayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent,
            WindowManagerTypes.AccessibilityOverlay,
            WindowManagerFlags.LayoutInScreen,
            Format.Translucent);
        WindowManager().AddView(layout, parameters);
        _overlay = layout;
    }

    private void HideOverlay()
    {
        if (_overlay == null) return;
        WindowManager().RemoveView(_overlay);
        _overlay = null;
    }

    private IWindowManager WindowManager() => GetSystemService(WindowService)!.JavaCast<IWindowManager>()!;

    private TextView Text(string text, float size, Color color)
    {
        var view = new TextView(this) { Text = text, TextSize = size, Gravity = GravityFlags.Center };
        view.SetTextColor(color);
        view.SetPadding(0, 16, 0, 16);
        return view;
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
