using Android.App;
using Android.App.Usage;
using Android.Content;
using Android.Content.PM;
using FocusTracker.Mobile.Services;

namespace FocusTracker.Mobile.Platforms.Android;

// Час по програмах з UsageStatsManager: коли яка програма була на екрані
public static class UsageCollector
{
    public static bool HasPermission(Context context)
    {
        var appOps = (AppOpsManager)context.GetSystemService(Context.AppOpsService)!;
        var mode = appOps.UnsafeCheckOpNoThrow(AppOpsManager.OpstrGetUsageStats!, global::Android.OS.Process.MyUid(), context.PackageName!);
        return mode == AppOpsManagerMode.Allowed;
    }

    // Відкриває системний екран «Доступ до історії використання»
    public static void RequestPermission(Context context)
    {
        var intent = new Intent(global::Android.Provider.Settings.ActionUsageAccessSettings);
        intent.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(intent);
    }

    public static UsageSnapshot Collect(Context context, DateTime fromLocal, DateTime toLocal)
    {
        var manager = (UsageStatsManager)context.GetSystemService(Context.UsageStatsService)!;
        var events = manager.QueryEvents(ToMillis(fromLocal), ToMillis(toLocal))!;
        var ignored = IgnoredPackages(context);
        var labels = new Dictionary<string, string>();

        var intervals = new List<UsageInterval>();
        string? current = null;
        DateTime currentStart = default;
        DateTime? lastActivity = null;

        void Close(DateTime at)
        {
            if (current != null && at > currentStart)
                intervals.Add(new UsageInterval(Label(context, current, labels), currentStart, at));
            current = null;
        }

        var e = new UsageEvents.Event();
        while (events.HasNextEvent)
        {
            events.GetNextEvent(e);
            var at = FromMillis(e.TimeStamp);
            var package = e.PackageName ?? "";

            switch (e.EventType)
            {
                case UsageEventType.ActivityResumed:
                    lastActivity = at;
                    if (package == current) break;
                    Close(at);
                    if (!ignored.Contains(package))
                    {
                        current = package;
                        currentStart = at;
                    }
                    break;
                case UsageEventType.ActivityPaused:
                case UsageEventType.ActivityStopped:
                    if (package == current) Close(at);
                    break;
                case UsageEventType.ScreenNonInteractive:
                case UsageEventType.KeyguardShown:
                    Close(at);
                    break;
                case UsageEventType.ScreenInteractive:
                case UsageEventType.KeyguardHidden:
                    lastActivity = at;
                    break;
            }
        }

        // Програма досі відкрита — рахуємо до «зараз»
        var foreground = current != null ? Label(context, current, labels) : null;
        if (current != null)
        {
            lastActivity = toLocal;
            Close(toLocal);
        }

        var (hours, minutes) = UsageAggregator.Aggregate(intervals);
        var packages = labels.GroupBy(l => l.Value).ToDictionary(g => g.Key, g => g.First().Key);
        return new UsageSnapshot(hours, minutes, lastActivity?.ToUniversalTime(), foreground, packages);
    }

    // Іконка програми як PNG 64×64 — так само, як ПК зберігає іконки з exe
    public static byte[]? IconPng(Context context, string package)
    {
        try
        {
            var drawable = context.PackageManager!.GetApplicationIcon(package);
            const int size = 64;
            using var bitmap = global::Android.Graphics.Bitmap.CreateBitmap(size, size, global::Android.Graphics.Bitmap.Config.Argb8888!)!;
            using var canvas = new global::Android.Graphics.Canvas(bitmap);
            drawable.SetBounds(0, 0, size, size);
            drawable.Draw(canvas);

            using var stream = new MemoryStream();
            bitmap.Compress(global::Android.Graphics.Bitmap.CompressFormat.Png!, 100, stream);
            return stream.ToArray();
        }
        catch (PackageManager.NameNotFoundException)
        {
            return null;
        }
    }

    // Робочий стіл, системна шторка й сам FocusTracker — не «програми», їх не рахуємо
    private static HashSet<string> IgnoredPackages(Context context)
    {
        var ignored = new HashSet<string> { context.PackageName!, "com.android.systemui" };
        var home = new Intent(Intent.ActionMain).AddCategory(Intent.CategoryHome);
        var launcher = context.PackageManager!.ResolveActivity(home, PackageInfoFlags.MatchDefaultOnly);
        if (launcher?.ActivityInfo?.PackageName is { } package)
            ignored.Add(package);
        return ignored;
    }

    private static string Label(Context context, string package, Dictionary<string, string> cache)
    {
        if (cache.TryGetValue(package, out var label)) return label;
        try
        {
            var info = context.PackageManager!.GetApplicationInfo(package, PackageInfoFlags.MetaData);
            label = context.PackageManager.GetApplicationLabel(info) ?? package;
        }
        catch (PackageManager.NameNotFoundException)
        {
            label = package;
        }
        return cache[package] = label;
    }

    private static long ToMillis(DateTime local) => new DateTimeOffset(local).ToUnixTimeMilliseconds();

    private static DateTime FromMillis(long millis) => DateTimeOffset.FromUnixTimeMilliseconds(millis).LocalDateTime;
}
