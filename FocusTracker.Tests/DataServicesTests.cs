using Xunit;
using Microsoft.EntityFrameworkCore.Infrastructure;
using FocusTracker.Data.Services;
using FocusTracker.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace FocusTracker.Tests;

public class DataServicesTests
{
    private static (SkillCategory category, Skill skill) SeedSkill(TestDb db)
    {
        var categories = new SkillCategoryService(db);
        var category = new SkillCategory { Name = "Dev", Color = "#fff" };
        categories.Add(category);

        var skills = new SkillService(db);
        var skill = new Skill { Name = "C#", CategoryId = category.Id, Level = 1 };
        skills.Add(skill);
        return (category, skill);
    }

    [Fact]
    public void Migrations_apply_to_empty_database()
    {
        using var db = new TestDb();
        using var ctx = db.CreateDbContext();
        Assert.Empty(ctx.Database.GetPendingMigrations());
    }

    [Fact]
    public void Task_with_loaded_programs_can_be_updated_and_deleted()
    {
        using var db = new TestDb();
        var (_, skill) = SeedSkill(db);

        var program = new TrackedProgram { Name = "code", Identifier = "code", Category = "Продуктивні" };
        new TrackedProgramService(db).Add(program);

        var tasks = new TaskItemService(db);
        var task = tasks.Add(new TaskItem { Title = "t", Description = "", SkillId = skill.Id, Difficulty = 1, DateCreated = DateTime.Now });
        new TaskProgramService(db).Add(new TaskProgram { TaskId = task.Id, ProgramId = program.Id });

        // Так робить UI: підвантажує навичку і програми у сутність задачі
        task.Skill = skill;
        task.Programs = new List<TrackedProgram> { program };
        task.Title = "renamed";
        tasks.Update(task);

        Assert.Equal("renamed", tasks.GetById(task.Id)!.Title);

        tasks.Delete(task.Id);
        Assert.Null(tasks.GetById(task.Id));
        Assert.Single(new TrackedProgramService(db).GetAll());
    }

    [Fact]
    public void Updating_task_does_not_overwrite_xp_added_meanwhile()
    {
        using var db = new TestDb();
        var (_, skill) = SeedSkill(db);
        var skills = new SkillService(db);
        var tasks = new TaskItemService(db);

        var task = tasks.Add(new TaskItem { Title = "t", Description = "", SkillId = skill.Id, Difficulty = 1, DateCreated = DateTime.Now });
        task.Skill = skill; // застаріла копія навички з Xp = 0

        skills.AddXp(skill.Id, 50);
        task.Completed = true;
        tasks.Update(task);

        Assert.Equal(50, skills.GetAll().Single().Xp);
    }

    [Fact]
    public async Task Updating_restriction_replaces_rules_and_targets()
    {
        using var db = new TestDb();
        var programs = new TrackedProgramService(db);
        var a = new TrackedProgram { Name = "a", Identifier = "a", Category = "Непродуктивні" };
        var b = new TrackedProgram { Name = "b", Identifier = "b", Category = "Непродуктивні" };
        programs.Add(a);
        programs.Add(b);

        var service = new RestrictionService(db);
        await service.CreateAsync(new Restriction
        {
            Note = "old",
            Rules = new List<RestrictionRule> { new() { RuleType = "max_total_seconds", Value = "30" } },
            Targets = new List<RestrictionToProgram> { new() { ProgramId = a.Id } }
        });

        var loaded = (await service.GetAllAsync()).Single();
        loaded.Note = "new";
        loaded.Rules.Clear();
        loaded.Rules.Add(new RestrictionRule { RuleType = "time_interval", Value = "22:00–23:00" });
        loaded.Targets.Clear();
        loaded.Targets.Add(new RestrictionToProgram { ProgramId = b.Id });
        await service.UpdateAsync(loaded);

        var updated = (await service.GetAllAsync()).Single();
        Assert.Equal("new", updated.Note);
        Assert.Equal("time_interval", Assert.Single(updated.Rules).RuleType);
        Assert.Equal("b", Assert.Single(updated.Targets).Program.Identifier);
    }

    [Fact]
    public async Task Usage_is_accumulated_per_day_and_hour()
    {
        using var db = new TestDb();
        var stats = new AppUsageStatService(db);

        await stats.AddOrUpdateUsageAsync("code", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        await stats.AddOrUpdateUsageAsync("code", TimeSpan.FromSeconds(5), TimeSpan.Zero);

        var today = Assert.Single(await stats.GetStatsForTodayAsync());
        Assert.Equal(TimeSpan.FromSeconds(10), today.TotalTime);
        Assert.Equal(TimeSpan.FromSeconds(5), today.ActiveTime);
        Assert.Single(new TrackedProgramService(db).GetAll());
    }

    [Fact]
    public async Task Daily_limit_restriction_is_detected()
    {
        using var db = new TestDb();
        var program = new TrackedProgram { Name = "game", Identifier = "game", Category = "Непродуктивні" };
        new TrackedProgramService(db).Add(program);
        await new RestrictionService(db).CreateAsync(new Restriction
        {
            Note = "limit",
            Rules = new List<RestrictionRule> { new() { RuleType = "max_total_seconds", Value = "1" } },
            Targets = new List<RestrictionToProgram> { new() { ProgramId = program.Id } }
        });

        var stats = new AppUsageStatService(db);
        var evaluator = new RestrictionEvaluator(db);

        await stats.AddOrUpdateUsageAsync("game", TimeSpan.FromSeconds(30), TimeSpan.Zero);
        Assert.False(evaluator.TryGetViolatedRestrictions("game", TimeSpan.Zero, out _));

        await stats.AddOrUpdateUsageAsync("game", TimeSpan.FromSeconds(60), TimeSpan.Zero);
        Assert.True(evaluator.TryGetViolatedRestrictions("game", TimeSpan.Zero, out var notes));
        Assert.Contains(notes, n => n.StartsWith("limit"));
    }
}

public class MigrationTests
{
    [Fact]
    public void Old_database_with_task_program_links_upgrades_and_keeps_programs()
    {
        var path = Path.Combine(Path.GetTempPath(), $"focustracker-old-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<FocusTracker.Data.FocusTrackerDbContext>()
            .UseSqlite($"Data Source={path};Pooling=False")
            .Options;
        try
        {
            using (var db = new FocusTracker.Data.FocusTrackerDbContext(options))
            {
                // Схема, як у версії з диплома
                db.Database.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>()
                    .Migrate("20250531170546_AddCountedPassiveSecondsToTaskProgramUsage123");

                db.Database.ExecuteSqlRaw("INSERT INTO SkillCategories (Id, Name, Color, SortOrder) VALUES (1, 'Dev', '#fff', 0)");
                db.Database.ExecuteSqlRaw("INSERT INTO Skills (Id, Name, CategoryId, Level, Xp) VALUES (1, 'C#', 1, 1, 0)");
                db.Database.ExecuteSqlRaw("INSERT INTO TaskItems (Id, Title, Description, SkillId, EstimatedMinutes, Difficulty, DateCreated, Completed, IsPassive) VALUES (1, 't', '', 1, 30, 1, '2025-05-31 10:00:00', 0, 0)");
                db.Database.ExecuteSqlRaw("INSERT INTO TrackedPrograms (Id, Name, Identifier, Category, IsTracked, IsHidden, TaskItemId) VALUES (1, 'code', 'code', 'Продуктивні', 1, 0, 1)");
            }

            using (var db = new FocusTracker.Data.FocusTrackerDbContext(options))
            {
                db.Database.Migrate();
                Assert.Equal("code", db.TrackedPrograms.Single().Identifier);
                db.TaskItems.Remove(db.TaskItems.Single());
                db.SaveChanges();
                Assert.Empty(db.TaskItems);
            }
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }
}
