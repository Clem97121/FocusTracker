namespace FocusTracker.Mobile.Services;

// Налаштування телефону й вхід: адреса проєкту, ключ, сесія (refresh-токен — у SecureStorage)
public class PhoneAccount
{
    // Проєкт Supabase за замовчуванням. Publishable key призначений для клієнтських застосунків:
    // без входу власника правила RLS не відкривають жодних даних.
    public const string DefaultProjectUrl = "https://jpzgqepfyolrwzknheyc.supabase.co";
    public const string DefaultPublishableKey = "sb_publishable_YE-LdL0gwqdpBc-3rPlMCg_MXGqO1w5";

    private const string RefreshTokenKey = "supabase.refresh_token";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static AuthSession? _session;

    public string ProjectUrl
    {
        get => Preferences.Get("supabase.url", "");
        private set => Preferences.Set("supabase.url", value);
    }

    public string PublishableKey
    {
        get => Preferences.Get("supabase.key", "");
        private set => Preferences.Set("supabase.key", value);
    }

    public string Email
    {
        get => Preferences.Get("supabase.email", "");
        private set => Preferences.Set("supabase.email", value);
    }

    // Ідентифікатор телефону в ft_devices. Виводиться з ANDROID_ID, тож переживає перевстановлення
    // (для того самого ключа підпису): інакше після кожного перевстановлення з'являвся б новий «Телефон»
    // і його час дублювався зі старим.
    public Guid DeviceId => StableDeviceId();

    // Ідентифікатор, яким телефон користувався до стабільного (випадковий з першої версії), — його треба прибрати з хмари
    public Guid? PreviousDeviceId =>
        Guid.TryParse(Preferences.Get("device.id", ""), out var id) && id != DeviceId ? id : null;

    public void ForgetPreviousDeviceId()
    {
        Preferences.Remove("device.id");
        // Токен push був прив'язаний до старого запису й видалився разом із ним — відправимо знову
        Preferences.Remove("push.token_uploaded");
    }

    private static Guid StableDeviceId()
    {
        var androidId = global::Android.Provider.Settings.Secure.GetString(
            global::Android.App.Application.Context.ContentResolver, global::Android.Provider.Settings.Secure.AndroidId) ?? "unknown";
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("focustracker:" + androidId));
        return new Guid(hash.AsSpan(0, 16));
    }

    public string DeviceName
    {
        get => Preferences.Get("device.name", "Телефон");
        set => Preferences.Set("device.name", string.IsNullOrWhiteSpace(value) ? "Телефон" : value.Trim());
    }

    public bool IsConfigured => ProjectUrl.Length > 0 && PublishableKey.Length > 0 && Email.Length > 0;

    public SupabaseApi Api() => new(ProjectUrl, PublishableKey);

    public async Task SignInAsync(string project, string key, string email, string password)
    {
        var url = SupabaseApi.NormalizeProjectUrl(project);
        var session = await new SupabaseApi(url, key.Trim()).SignInAsync(email.Trim(), password);

        ProjectUrl = url;
        PublishableKey = key.Trim();
        Email = session.Email;
        await SecureStorage.Default.SetAsync(RefreshTokenKey, session.RefreshToken);
        _session = session;
    }

    public void SignOut()
    {
        SecureStorage.Default.Remove(RefreshTokenKey);
        Preferences.Remove("supabase.email");
        _session = null;
    }

    // Чинний access-токен; оновлює сесію, якщо вона закінчується
    public async Task<string> GetAccessTokenAsync()
    {
        await Gate.WaitAsync();
        try
        {
            if (_session is { } current && current.ExpiresAtUtc > DateTime.UtcNow.AddMinutes(1))
                return current.AccessToken;

            var refresh = _session?.RefreshToken ?? await SecureStorage.Default.GetAsync(RefreshTokenKey)
                ?? throw new SupabaseException("Потрібно увійти знову.");
            _session = await Api().RefreshAsync(refresh);
            // Refresh-токени одноразові: зберігаємо новий
            await SecureStorage.Default.SetAsync(RefreshTokenKey, _session.RefreshToken);
            return _session.AccessToken;
        }
        finally
        {
            Gate.Release();
        }
    }
}
