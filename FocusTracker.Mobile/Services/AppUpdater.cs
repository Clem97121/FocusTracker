using System.Text.Json;

namespace FocusTracker.Mobile.Services;

public record AppUpdate(Version Version, string DownloadUrl);

// Оновлення з GitHub Releases: у кожному релізі поруч із Setup.exe лежить FocusTracker-android.apk
public static class AppUpdater
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/Clem97121/FocusTracker/releases/latest";
    private const string ApkName = "FocusTracker-android.apk";
    private static readonly HttpClient Http = CreateClient();

    public static Version CurrentVersion => Version.TryParse(AppInfo.Current.VersionString, out var v) ? v : new Version(0, 0);

    // Нова версія або null. У Debug-збірці не перевіряємо: її підпис інший, і поверх неї реліз не стане.
    public static async Task<AppUpdate?> CheckAsync()
    {
#if DEBUG
        await Task.CompletedTask;
        return null;
#else
        using var json = JsonDocument.Parse(await Http.GetStringAsync(LatestReleaseUrl));
        var root = json.RootElement;
        if (!Version.TryParse(root.GetProperty("tag_name").GetString()?.TrimStart('v'), out var latest) || latest <= CurrentVersion)
            return null;

        var apk = root.GetProperty("assets").EnumerateArray()
            .FirstOrDefault(a => a.GetProperty("name").GetString() == ApkName);
        return apk.ValueKind == JsonValueKind.Object
            ? new AppUpdate(latest, apk.GetProperty("browser_download_url").GetString()!)
            : null;
#endif
    }

    // Завантажує APK у кеш застосунку й повертає шлях до файлу
    public static async Task<string> DownloadAsync(AppUpdate update, IProgress<double>? progress = null)
    {
        var dir = Path.Combine(FileSystem.CacheDirectory, "updates");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, ApkName);

        using var response = await Http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? 0;

        await using var source = await response.Content.ReadAsStreamAsync();
        await using var target = File.Create(path);
        var buffer = new byte[81920];
        long read = 0;
        int count;
        while ((count = await source.ReadAsync(buffer)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, count));
            read += count;
            if (total > 0) progress?.Report((double)read / total);
        }
        return path;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        // GitHub API вимагає User-Agent
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FocusTracker-Android");
        return client;
    }
}
