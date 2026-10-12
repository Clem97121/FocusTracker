using Xunit;
using FocusTracker.Data;
using FocusTracker.Data.Cloud;
using FocusTracker.Data.Services;
using FocusTracker.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace FocusTracker.Tests;

public class EntitySyncTests
{
    private static readonly Guid Laptop = Guid.NewGuid();

    private static CloudEntity Entity(string kind, Guid id, string? json, DateTime updatedAt, bool deleted = false) =>
        new(kind, id, json, deleted, updatedAt, Laptop);

    [Fact]
    public void Local_changes_get_sync_id_time_and_tombstones()
    {
        using var db = new TestDb();
        var categories = new SkillCategoryService(db);
        var category = new SkillCategory { Name = "Dev", Color = "#fff" };
        categories.Add(category);

        using (var ctx = db.CreateDbContext())
        {
            var saved = ctx.SkillCategories.Single();
            Assert.NotEqual(Guid.Empty, saved.SyncId);
            Assert.NotNull(saved.SyncUpdatedAt);
        }

        categories.Delete(category.Id);

        using (var ctx = db.CreateDbContext())
        {
            var tombstone = ctx.SyncTombstones.Single();
            Assert.Equal(SyncKinds.Category, tombstone.Kind);
            Assert.Equal(category.SyncId, tombstone.SyncId);
        }
    }

    [Fact]
    public async Task Changing_task_programs_marks_task_as_changed()
    {
        using var db = new TestDb();
        var category = new SkillCategory { Name = "Dev", Color = "#fff" };
        new SkillCategoryService(db).Add(category);
        var skill = new Skill { Name = "C#", CategoryId = category.Id, Level = 1 };
        new SkillService(db).Add(skill);
        var task = new TaskItemService(db).Add(new TaskItem { Title = "t", Description = "", SkillId = skill.Id, DateCreated = DateTime.Now });
        var program = new TrackedProgram { Name = "code", Identifier = "code", Category = "Продуктивні" };
        new TrackedProgramService(db).Add(program);

        DateTime? before;
        using (var ctx = db.CreateDbContext())
            before = (await ctx.TaskItems.SingleAsync()).SyncUpdatedAt;

        await Task.Delay(20);
        new TaskProgramService(db).Add(new TaskProgram { TaskId = task.Id, ProgramId = program.Id });

        using (var ctx = db.CreateDbContext())
            Assert.True((await ctx.TaskItems.SingleAsync()).SyncUpdatedAt > before);
    }

    [Fact]
    public async Task Remote_entities_are_created_updated_and_deleted()
    {
        using var db = new TestDb();
        var sync = new CloudSyncService(db);
        var (categoryId, skillId, taskId, restrictionId) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var t0 = DateTime.UtcNow.AddMinutes(-10);

        var skipped = await sync.ApplyRemoteEntitiesAsync(new[]
        {
            // Порядок навмисно переплутаний: застосування саме впорядковує батьків раніше
            Entity(SyncKinds.Task, taskId, $$"""{"title":"Курс","description":"","skillId":"{{skillId}}","estimatedMinutes":30,"difficulty":2,"dateCreated":"2026-10-12T10:00:00","completed":false,"earnedXp":null,"activeSeconds":null,"isPassive":false,"programs":["code"]}""", t0),
            Entity(SyncKinds.Skill, skillId, $$"""{"name":"C#","categoryId":"{{categoryId}}","level":3,"xp":40}""", t0),
            Entity(SyncKinds.Category, categoryId, """{"name":"Dev","color":"#fff","sortOrder":1}""", t0),
            Entity(SyncKinds.Restriction, restrictionId, """{"note":"YouTube","rules":[{"ruleType":"max_total_seconds","value":"60"}],"programs":["chrome"]}""", t0)
        });
        Assert.Equal(0, skipped);

        using (var ctx = db.CreateDbContext())
        {
            var skill = await ctx.Skills.Include(s => s.Category).SingleAsync();
            Assert.Equal("Dev", skill.Category.Name);
            Assert.Equal(3, skill.Level);
            Assert.Equal(t0, skill.SyncUpdatedAt);
            var task = await ctx.TaskItems.SingleAsync();
            Assert.Equal(skill.Id, task.SkillId);
            Assert.Equal("code", (await ctx.TaskPrograms.Include(tp => tp.Program).SingleAsync()).Program.Identifier);
            var restriction = await ctx.Restrictions.Include(r => r.Rules).Include(r => r.Targets).ThenInclude(t => t.Program).SingleAsync();
            Assert.Equal("60", restriction.Rules.Single().Value);
            Assert.Equal("chrome", restriction.Targets.Single().Program.Identifier);
            Assert.Empty(ctx.SyncTombstones);
        }

        // Старіша зміна ігнорується, новіша застосовується
        await sync.ApplyRemoteEntitiesAsync(new[] { Entity(SyncKinds.Skill, skillId, $$"""{"name":"Old","categoryId":"{{categoryId}}","level":1,"xp":0}""", t0.AddMinutes(-1)) });
        await sync.ApplyRemoteEntitiesAsync(new[] { Entity(SyncKinds.Skill, skillId, $$"""{"name":"C# Pro","categoryId":"{{categoryId}}","level":4,"xp":5}""", t0.AddMinutes(1)) });
        using (var ctx = db.CreateDbContext())
            Assert.Equal("C# Pro", (await ctx.Skills.SingleAsync()).Name);

        // Видалення з іншого пристрою
        await sync.ApplyRemoteEntitiesAsync(new[] { Entity(SyncKinds.Restriction, restrictionId, null, t0.AddMinutes(2), deleted: true) });
        using (var ctx = db.CreateDbContext())
        {
            Assert.Empty(ctx.Restrictions);
            Assert.Empty(ctx.SyncTombstones);
        }
    }

    [Fact]
    public async Task Entity_without_parent_is_skipped()
    {
        using var db = new TestDb();
        var skipped = await new CloudSyncService(db).ApplyRemoteEntitiesAsync(new[]
        {
            Entity(SyncKinds.Skill, Guid.NewGuid(), $$"""{"name":"C#","categoryId":"{{Guid.NewGuid()}}","level":1,"xp":0}""", DateTime.UtcNow)
        });

        Assert.Equal(1, skipped);
        using var ctx = db.CreateDbContext();
        Assert.Empty(ctx.Skills);
    }
}
