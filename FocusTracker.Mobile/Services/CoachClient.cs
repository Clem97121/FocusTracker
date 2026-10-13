using System.Text.Json;

namespace FocusTracker.Mobile.Services;

public record CoachMessage(long Id, DateTime CreatedAtUtc, string Role, string Kind, string Text,
    Guid? TargetDeviceId, string Status, string? Error);

// Спільний чат із Коучем (ft_assistant_messages): ті самі повідомлення, що й на ПК.
// Повідомлення користувача будить хмарну функцію через тригер у базі.
public class CoachClient
{
    private const string Columns = "select=id,created_at,role,kind,text,target_device_id,status,error";
    private readonly PhoneAccount _account = new();

    public async Task<List<CoachMessage>> RecentAsync(int count = 50)
    {
        var messages = await QueryAsync($"{Columns}&order=id.desc&limit={count}");
        messages.Reverse();
        return messages;
    }

    public Task<List<CoachMessage>> AfterAsync(long afterId) =>
        QueryAsync($"{Columns}&id=gt.{afterId}&order=id.asc&limit=100");

    public async Task<long> SendAsync(string text)
    {
        var token = await _account.GetAccessTokenAsync();
        using var inserted = await _account.Api().InsertReturningAsync("ft_assistant_messages", new
        {
            role = "user",
            kind = "chat",
            text,
            device_id = _account.DeviceId,
            status = "pending",
        }, token, "id");
        return inserted.RootElement[0].GetProperty("id").GetInt64();
    }

    // Чекає, поки функція обробить повідомлення; null — не дочекались
    public async Task<CoachMessage?> WaitProcessedAsync(long id, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            var message = (await QueryAsync($"{Columns}&id=eq.{id}")).FirstOrDefault();
            if (message != null && message.Status != "pending")
                return message;
        }
        return null;
    }

    private async Task<List<CoachMessage>> QueryAsync(string query)
    {
        var token = await _account.GetAccessTokenAsync();
        using var rows = await _account.Api().SelectAsync("ft_assistant_messages", query, token);
        return rows.RootElement.EnumerateArray().Select(Parse).ToList();
    }

    private static CoachMessage Parse(JsonElement r) => new(
        r.GetProperty("id").GetInt64(),
        r.GetProperty("created_at").GetDateTime().ToUniversalTime(),
        r.GetProperty("role").GetString()!,
        r.GetProperty("kind").GetString()!,
        r.GetProperty("text").GetString()!,
        r.GetProperty("target_device_id").ValueKind == JsonValueKind.String ? r.GetProperty("target_device_id").GetGuid() : null,
        r.GetProperty("status").GetString()!,
        r.GetProperty("error").ValueKind == JsonValueKind.String ? r.GetProperty("error").GetString() : null);
}
