using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace FocusTracker.Mobile.Services;

public record AuthSession(string AccessToken, string RefreshToken, DateTime ExpiresAtUtc, Guid UserId, string Email);

public class SupabaseException(string message) : Exception(message);

// Мінімальний клієнт Supabase: вхід (GoTrue) і таблиці (PostgREST) через HTTP.
// Телефон ходить під обліковим записом власника; доступ обмежують правила RLS у базі.
public class SupabaseApi
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly string _url;
    private readonly string _key;

    public SupabaseApi(string projectUrl, string publishableKey)
    {
        _url = projectUrl.TrimEnd('/');
        _key = publishableKey;
    }

    // «abcdefghijklmnopqrst» або повна адреса → https://<ref>.supabase.co
    public static string NormalizeProjectUrl(string value)
    {
        value = value.Trim().TrimEnd('/');
        if (value.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return value;
        return $"https://{value}.supabase.co";
    }

    public Task<AuthSession> SignInAsync(string email, string password) =>
        TokenAsync("password", new { email, password });

    public Task<AuthSession> RefreshAsync(string refreshToken) =>
        TokenAsync("refresh_token", new { refresh_token = refreshToken });

    private async Task<AuthSession> TokenAsync(string grantType, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_url}/auth/v1/token?grant_type={grantType}")
        {
            Content = JsonContent.Create(body, options: Json)
        };
        request.Headers.Add("apikey", _key);

        using var response = await Http.SendAsync(request);
        var json = await ReadAsync(response);
        var root = json.RootElement;
        var user = root.GetProperty("user");
        return new AuthSession(
            root.GetProperty("access_token").GetString()!,
            root.GetProperty("refresh_token").GetString()!,
            DateTime.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32()),
            Guid.Parse(user.GetProperty("id").GetString()!),
            user.TryGetProperty("email", out var email) ? email.GetString() ?? "" : "");
    }

    // Вставка або оновлення рядків за ключем onConflict (стовпці через кому)
    public async Task UpsertAsync(string table, string onConflict, object rows, string accessToken)
    {
        using var request = Request(HttpMethod.Post, $"{table}?on_conflict={onConflict}", accessToken);
        request.Content = JsonContent.Create(rows, options: Json);
        request.Headers.Add("Prefer", "resolution=merge-duplicates,return=minimal");
        using var response = await Http.SendAsync(request);
        await EnsureSuccessAsync(response);
    }

    public async Task InsertAsync(string table, object row, string accessToken)
    {
        using var request = Request(HttpMethod.Post, table, accessToken);
        request.Content = JsonContent.Create(row, options: Json);
        request.Headers.Add("Prefer", "return=minimal");
        using var response = await Http.SendAsync(request);
        await EnsureSuccessAsync(response);
    }

    // query — частина адреси PostgREST після «?», напр. "select=a,b&date=eq.2026-10-13"
    public async Task<JsonDocument> SelectAsync(string table, string query, string accessToken)
    {
        using var request = Request(HttpMethod.Get, $"{table}?{query}", accessToken);
        using var response = await Http.SendAsync(request);
        return await ReadAsync(response);
    }

    private HttpRequestMessage Request(HttpMethod method, string path, string accessToken)
    {
        var request = new HttpRequestMessage(method, $"{_url}/rest/v1/{path}");
        request.Headers.Add("apikey", _key);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response)
    {
        await EnsureSuccessAsync(response);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;

        var text = await response.Content.ReadAsStringAsync();
        var message = text;
        try
        {
            using var json = JsonDocument.Parse(text);
            foreach (var field in new[] { "error_description", "msg", "message", "error" })
            {
                if (json.RootElement.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String)
                {
                    message = value.GetString()!;
                    break;
                }
            }
        }
        catch (JsonException)
        {
        }
        throw new SupabaseException($"{(int)response.StatusCode}: {message}");
    }

    // bytea у JSON PostgREST — рядок "\x" + hex
    public static string ToBytea(byte[] bytes)
    {
        var text = new StringBuilder(2 + bytes.Length * 2).Append("\\x");
        foreach (var b in bytes) text.Append(b.ToString("x2"));
        return text.ToString();
    }
}
