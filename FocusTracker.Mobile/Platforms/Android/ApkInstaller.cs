using Android.Content;

namespace FocusTracker.Mobile.Platforms.Android;

// Відкриває системне встановлення завантаженого APK
public static class ApkInstaller
{
    // Android вимагає окремого дозволу «Встановлення невідомих програм» для цього застосунку
    public static bool CanInstall(Context context) => context.PackageManager!.CanRequestPackageInstalls();

    public static void RequestInstallPermission(Context context)
    {
        var intent = new Intent(global::Android.Provider.Settings.ActionManageUnknownAppSources,
            global::Android.Net.Uri.Parse($"package:{context.PackageName}"));
        intent.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(intent);
    }

    // MAUI сам віддає файл інсталятору через свій FileProvider
    public static Task InstallAsync(string apkPath) =>
        Launcher.Default.OpenAsync(new OpenFileRequest("Оновлення FocusTracker",
            new ReadOnlyFile(apkPath, "application/vnd.android.package-archive")));
}
