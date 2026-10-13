using System.Text.Json;
using Xunit;
using FocusTracker.Data;
using FocusTracker.Data.Cloud;
using FocusTracker.Domain;
using FocusTracker.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace FocusTracker.Tests;

public class SupabaseProjectTests
{
    [Theory]
    // Session pooler (формат .NET і URI)
    [InlineData("Host=aws-0-eu-central-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.abcdefghijklmnopqrst;Password=p@ss;SSL Mode=Require", "abcdefghijklmnopqrst")]
    [InlineData("User Id=postgres.qwertyuiopasdfghjklz;Password=x;Server=aws-1-eu-west-2.pooler.supabase.com;Port=6543;Database=postgres", "qwertyuiopasdfghjklz")]
    [InlineData("postgresql://postgres.abcdefghijklmnopqrst:secret@aws-0-eu-central-1.pooler.supabase.com:5432/postgres", "abcdefghijklmnopqrst")]
    // Пряме підключення
    [InlineData("Host=db.zyxwvutsrqponmlkjihg.supabase.co;Port=5432;Database=postgres;Username=postgres;Password=x", "zyxwvutsrqponmlkjihg")]
    [InlineData("postgresql://postgres:secret@db.zyxwvutsrqponmlkjihg.supabase.co:5432/postgres", "zyxwvutsrqponmlkjihg")]
    public void Ref_is_taken_from_pooler_user_or_direct_host(string connectionString, string expected)
    {
        Assert.Equal(expected, SupabaseProject.GetRef(connectionString));
        Assert.Equal($"https://{expected}.supabase.co/functions/v1/assistant", SupabaseProject.GetFunctionUrl(connectionString));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Host=localhost;Username=postgres;Password=x")]
    [InlineData("Host=db.example.com;Username=postgres;Password=x")]
    [InlineData("not a connection string")]
    public void Unknown_connection_has_no_ref(string? connectionString)
    {
        Assert.Null(SupabaseProject.GetRef(connectionString));
        Assert.Null(SupabaseProject.GetFunctionUrl(connectionString));
    }
}

public class AssistantCloudSettingsTests
{
    [Fact]
    public void Settings_json_has_fields_the_function_reads()
    {
        var settings = new AppSettings
        {
            Goals = "1. Робота",
            JarvisRules = "без ігор",
            ProductiveFromHour = 10,
            ProductiveToHour = 19,
            JarvisCheckMinutes = 15,
            JarvisMayStartFocus = false,
            JarvisModel = "claude-sonnet-5-5",
            JarvisEnabled = true,
            AssistantInCloud = true,
        };

        using var doc = JsonDocument.Parse(AssistantCloudSettings.ToJson(settings, TimeZoneInfo.Utc));
        var root = doc.RootElement;
        Assert.Equal("1. Робота", root.GetProperty("goals").GetString());
        Assert.Equal("без ігор", root.GetProperty("rules").GetString());
        Assert.Equal(10, root.GetProperty("fromHour").GetInt32());
        Assert.Equal(19, root.GetProperty("toHour").GetInt32());
        Assert.Equal(15, root.GetProperty("checkMinutes").GetInt32());
        Assert.False(root.GetProperty("mayStartFocus").GetBoolean());
        Assert.Equal("claude-sonnet-5-5", root.GetProperty("model").GetString());
        Assert.True(root.GetProperty("enabled").GetBoolean());
        Assert.Equal(AssistantInfo.Name, root.GetProperty("name").GetString());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("timeZone").GetString()));
    }

    [Fact]
    public void Cloud_is_disabled_without_cloud_mode()
    {
        var settings = new AppSettings { JarvisEnabled = true, AssistantInCloud = false };
        using var doc = JsonDocument.Parse(AssistantCloudSettings.ToJson(settings, TimeZoneInfo.Utc));
        Assert.False(doc.RootElement.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public void Windows_time_zone_is_converted_to_iana()
    {
        var kyiv = TimeZoneInfo.CreateCustomTimeZone("FLE Standard Time", TimeSpan.FromHours(2), "Kyiv", "Kyiv");
        Assert.StartsWith("Europe/", AssistantCloudSettings.IanaId(kyiv));
        Assert.Equal("UTC", AssistantCloudSettings.IanaId(TimeZoneInfo.CreateCustomTimeZone("Nowhere", TimeSpan.Zero, "x", "x")));
    }
}

public class CloudAssistantTaskTests
{
    // Такий самий updated_by пише хмарна функція (supabase/functions/assistant/index.ts)
    private static readonly Guid CloudAssistant = Guid.Parse("00000000-0000-4000-8000-0000a5515700");

    [Fact]
    public async Task Task_added_by_cloud_assistant_is_created_locally()
    {
        using var db = new TestDb();
        var sync = new CloudSyncService(db);
        var (categoryId, skillId, taskId) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var t0 = DateTime.UtcNow.AddMinutes(-5);

        await sync.ApplyRemoteEntitiesAsync(new[]
        {
            new CloudEntity(SyncKinds.Category, categoryId, """{"name":"Кар'єра","color":"#fff","sortOrder":1}""", false, t0, Guid.NewGuid()),
            new CloudEntity(SyncKinds.Skill, skillId, $$"""{"name":"Пошук роботи","categoryId":"{{categoryId}}","level":1,"xp":0}""", false, t0, Guid.NewGuid()),
        });

        // Як jsonb повертає документ: інший порядок ключів і пробіли
        var json = $$"""{"title": "Відгукнутись на 3 вакансії", "skillId": "{{skillId}}", "programs": [], "completed": false, "earnedXp": null, "isPassive": false, "difficulty": 3, "dateCreated": "2026-10-13T14:05:00", "description": "", "activeSeconds": null, "estimatedMinutes": 45}""";
        var skipped = await sync.ApplyRemoteEntitiesAsync(new[]
        {
            new CloudEntity(SyncKinds.Task, taskId, json, false, t0.AddMinutes(1), CloudAssistant)
        });

        Assert.Equal(0, skipped);
        using var ctx = db.CreateDbContext();
        var task = await ctx.TaskItems.Include(t => t.Skill).SingleAsync();
        Assert.Equal("Відгукнутись на 3 вакансії", task.Title);
        Assert.Equal("Пошук роботи", task.Skill.Name);
        Assert.Equal(45, task.EstimatedMinutes);
        Assert.Equal(3, task.Difficulty);
        Assert.Equal(new DateTime(2026, 10, 13, 14, 5, 0), task.DateCreated);
        Assert.False(task.Completed);
        Assert.Equal(taskId, task.SyncId);
        Assert.Empty(ctx.TaskPrograms);
    }
}

public class RecentActivityChangesTests
{
    [Fact]
    public void Changed_since_returns_new_and_extended_entries()
    {
        var log = new RecentActivityLog();
        log.Add("chrome", "Djinni", TimeSpan.FromSeconds(30));
        var mark = DateTime.Now;
        Assert.Empty(log.ChangedSince(mark.AddSeconds(1)));

        log.Add("chrome", "Djinni", TimeSpan.FromSeconds(1));
        log.Add("code", "Program.cs", TimeSpan.FromSeconds(1));

        var changed = log.ChangedSince(mark.AddMilliseconds(-1));
        Assert.Equal(new[] { "chrome", "code" }, changed.Select(c => c.App));
        Assert.Equal(TimeSpan.FromSeconds(31), changed[0].Duration);
    }
}
