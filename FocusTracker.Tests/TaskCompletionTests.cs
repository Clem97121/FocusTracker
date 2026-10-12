using FocusTracker.Data.Services;
using FocusTracker.Domain.Interfaces;
using FocusTracker.Domain.Models;
using Xunit;

namespace FocusTracker.Tests;

public class TaskCompletionTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly SkillService _skills;
    private readonly TaskItemService _tasks;
    private readonly TaskCompletionService _completion;
    private readonly TrackedProgram _code = new() { Name = "code", Identifier = "code", Category = "Продуктивні" };
    private readonly Skill _skill;

    public TaskCompletionTests()
    {
        _skills = new SkillService(_db);
        _tasks = new TaskItemService(_db);
        _completion = new TaskCompletionService(_db, _skills);

        var category = new SkillCategory { Name = "Dev", Color = "#fff" };
        new SkillCategoryService(_db).Add(category);
        _skill = new Skill { Name = "C#", CategoryId = category.Id, Level = 1 };
        _skills.Add(_skill);
        new TrackedProgramService(_db).Add(_code);
    }

    private void SetDailyActive(DateTime date, int activeSeconds, int totalSeconds = -1)
    {
        using var ctx = _db.CreateDbContext();
        var stat = ctx.AppUsageStats.FirstOrDefault(s => s.AppName == "code" && s.Date == date.Date);
        if (stat == null)
        {
            stat = new AppUsageStat { AppName = "code", Date = date.Date };
            ctx.AppUsageStats.Add(stat);
        }
        stat.ActiveTime = TimeSpan.FromSeconds(activeSeconds);
        stat.TotalTime = TimeSpan.FromSeconds(totalSeconds < 0 ? activeSeconds : totalSeconds);
        ctx.SaveChanges();
    }

    private TaskItem CreateTask(DateTime created, int initialActive, int difficulty = 1, bool passive = false)
    {
        var task = _tasks.Add(new TaskItem
        {
            Title = "t", Description = "", SkillId = _skill.Id,
            Difficulty = difficulty, DateCreated = created, IsPassive = passive
        });
        new TaskProgramService(_db).Add(new TaskProgram { TaskId = task.Id, ProgramId = _code.Id });
        new TaskProgramUsageService(_db).AddOrUpdate(new TaskProgramUsage
        {
            TaskId = task.Id, ProgramId = _code.Id,
            InitialActiveSeconds = initialActive, InitialPassiveSeconds = initialActive,
            BaselineDate = created.Date
        });
        return task;
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Counts_only_time_after_task_creation()
    {
        var today = DateTime.Today;
        SetDailyActive(today, 600);
        var task = CreateTask(today.AddHours(10), initialActive: 600, difficulty: 2);

        SetDailyActive(today, 600 + 30 * 60); // +30 хвилин роботи

        Assert.Equal(60, _completion.Complete(task.Id)); // 30 хв × складність 2
        var saved = _tasks.GetById(task.Id)!;
        Assert.True(saved.Completed);
        Assert.Equal(30 * 60, saved.ActiveSeconds);
    }

    [Fact]
    public void Task_spanning_midnight_counts_both_days()
    {
        var yesterday = DateTime.Today.AddDays(-1);
        SetDailyActive(yesterday, 3600);
        var task = CreateTask(yesterday.AddHours(22), initialActive: 3600);

        SetDailyActive(yesterday, 3600 + 20 * 60); // 20 хв увечері
        SetDailyActive(DateTime.Today, 10 * 60);   // 10 хв після півночі

        Assert.Equal(30, _completion.Complete(task.Id));
    }

    [Fact]
    public void Task_without_new_time_completes_with_zero_xp()
    {
        SetDailyActive(DateTime.Today, 600);
        var task = CreateTask(DateTime.Now, initialActive: 600);

        Assert.Equal(0, _completion.Complete(task.Id));
        Assert.True(_tasks.GetById(task.Id)!.Completed);
        Assert.Equal(0, _skills.GetAll().Single().Xp);
    }

    [Fact]
    public void Time_is_not_counted_twice_by_two_tasks()
    {
        var today = DateTime.Today;
        SetDailyActive(today, 0);
        var first = CreateTask(today.AddHours(9), initialActive: 0);
        var second = CreateTask(today.AddHours(9).AddMinutes(1), initialActive: 0);

        SetDailyActive(today, 20 * 60);

        Assert.Equal(20, _completion.Complete(second.Id));
        Assert.Equal(0, _completion.Complete(first.Id));
    }

    [Fact]
    public void Passive_task_counts_total_time()
    {
        var today = DateTime.Today;
        SetDailyActive(today, 0, totalSeconds: 0);
        var task = CreateTask(today.AddHours(9), initialActive: 0, passive: true);

        SetDailyActive(today, activeSeconds: 60, totalSeconds: 40 * 60);

        Assert.Equal(40, _completion.Complete(task.Id));
    }

    [Fact]
    public void Xp_levels_up_with_consistent_formula()
    {
        // Рівень 1 → 2 коштує 100 XP, 2 → 3 коштує 200 XP
        _skills.AddXp(_skill.Id, 250);
        var skill = _skills.GetAll().Single();
        Assert.Equal(2, skill.Level);
        Assert.Equal(150, skill.Xp);
        Assert.Equal(200, skill.XpToNextLevel);
    }

    [Fact]
    public void Xp_is_one_per_minute_times_difficulty()
    {
        Assert.Equal(0, ITaskCompletionService.CalculateXp(0, 3));
        Assert.Equal(0, ITaskCompletionService.CalculateXp(59, 1));
        Assert.Equal(180, ITaskCompletionService.CalculateXp(3600, 3));
    }
}
