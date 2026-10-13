using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using FocusTracker.Mobile.Platforms.Android;

namespace FocusTracker.Mobile;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
	// Застосунок відкрили зі сповіщення Коуча — головна сторінка відкриє чат
	public static bool OpenChatRequested { get; set; }

	public static event Action? OpenChat;

	protected override void OnCreate(Bundle? savedInstanceState)
	{
		base.OnCreate(savedInstanceState);
		HandleIntent(Intent);
	}

	protected override void OnNewIntent(Intent? intent)
	{
		base.OnNewIntent(intent);
		HandleIntent(intent);
	}

	private static void HandleIntent(Intent? intent)
	{
		if (intent?.GetBooleanExtra(CoachNotifications.OpenChatExtra, false) != true) return;
		OpenChatRequested = true;
		OpenChat?.Invoke();
	}
}
