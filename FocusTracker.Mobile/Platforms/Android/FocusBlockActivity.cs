using Android.App;
using Button = Android.Widget.Button;
using Color = Android.Graphics.Color;
using Android.OS;
using Android.Views;
using Android.Widget;
using FocusTracker.Mobile.Services;

namespace FocusTracker.Mobile.Platforms.Android;

// Заглушка поверх заблокованої програми під час фокус-режиму
[Activity(Theme = "@android:style/Theme.DeviceDefault.NoActionBar", ExcludeFromRecents = true, LaunchMode = global::Android.Content.PM.LaunchMode.SingleTask)]
public class FocusBlockActivity : Activity
{
    public const string AppExtra = "blocked_app";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var app = Intent?.GetStringExtra(AppExtra) ?? "";
        var until = FocusState.UntilUtc?.ToLocalTime();

        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
        layout.SetGravity(GravityFlags.Center);
        layout.SetPadding(64, 64, 64, 64);
        layout.SetBackgroundColor(Color.ParseColor("#1E1E24"));

        layout.AddView(Text("Фокус-режим", 28, Color.White));
        layout.AddView(Text(until is { } u ? $"до {u:HH:mm}" : "", 18, Color.ParseColor("#9E9EA8")));
        layout.AddView(Text($"{app} зараз заблоковано.", 18, Color.White));
        if (FocusState.Reason.Length > 0)
            layout.AddView(Text(FocusState.Reason, 16, Color.ParseColor("#C8C8D0")));

        var ok = new Button(this) { Text = "Зрозуміло, повертаюсь до справ" };
        ok.Click += (_, _) => Finish();
        layout.AddView(ok);

        // Свідоме рішення: зняти фокус на всіх пристроях (Коуч це побачить)
        var stop = new Button(this) { Text = "Завершити фокус-режим" };
        stop.Click += async (_, _) =>
        {
            stop.Enabled = false;
            try
            {
                await FocusState.StopAsync(new PhoneAccount());
                Finish();
            }
            catch (Exception ex)
            {
                stop.Enabled = true;
                Toast.MakeText(this, $"Не вдалося: {ex.Message}", ToastLength.Long)?.Show();
            }
        };
        layout.AddView(stop);

        SetContentView(layout);
    }

    private TextView Text(string text, float size, Color color)
    {
        var view = new TextView(this) { Text = text, TextSize = size, Gravity = GravityFlags.Center };
        view.SetTextColor(color);
        view.SetPadding(0, 16, 0, 16);
        return view;
    }
}
