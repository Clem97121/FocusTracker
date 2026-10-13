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

    // Ідентифікатор телефону в ft_devices; створюється один раз
    public Guid DeviceId
    {
        get
        {
            var value = Preferences.Get("device.id", "");
            if (Guid.TryParse(value, out var id)) return id;
            id = Guid.NewGuid();
            Preferences.Set("device.id", id.ToString());
            return id;
        }
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
