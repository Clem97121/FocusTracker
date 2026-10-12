using Xunit;
using FocusTracker.Domain;

namespace FocusTracker.Tests;

public class RecentActivityLogTests
{
    [Fact]
    public void Consecutive_seconds_in_same_window_are_merged()
    {
        var log = new RecentActivityLog();
        for (int i = 0; i < 90; i++)
            log.Add("chrome", "Djinni — DevOps вакансії", TimeSpan.FromSeconds(1));
        log.Add("code", "Program.cs — FocusTracker", TimeSpan.FromSeconds(40));

        var text = log.Describe(TimeSpan.FromMinutes(60));

        Assert.Contains("(1 хв) chrome — Djinni — DevOps вакансії", text);
        Assert.Contains("(40 с) code — Program.cs — FocusTracker", text);
        Assert.Equal(2, text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void Short_switches_are_summarized_and_long_titles_cut()
    {
        var log = new RecentActivityLog();
        log.Add("telegram", "Чат", TimeSpan.FromSeconds(5));
        log.Add("discord", new string('x', 300), TimeSpan.FromSeconds(60));

        var text = log.Describe(TimeSpan.FromMinutes(60));

        Assert.Contains("ще 1 коротких перемикань", text);
        Assert.DoesNotContain(new string('x', 121), text);
    }

    [Fact]
    public void Empty_log_says_user_was_away()
    {
        Assert.Contains("немає даних", new RecentActivityLog().Describe(TimeSpan.FromMinutes(60)));
    }
}

public class FocusModeTests
{
    [Fact]
    public void Blocks_listed_apps_until_stopped()
    {
        var focus = new FocusMode();
        Assert.False(focus.Blocks("steam"));

        focus.Start(TimeSpan.FromMinutes(25), new[] { "steam", "Discord" }, "відгуки на вакансії");
        Assert.True(focus.IsActive);
        Assert.True(focus.Blocks("Steam"));
        Assert.True(focus.Blocks("discord"));
        Assert.False(focus.Blocks("chrome"));

        focus.Stop();
        Assert.False(focus.Blocks("steam"));
    }

    [Fact]
    public void Expired_focus_does_not_block()
    {
        var focus = new FocusMode();
        focus.Start(TimeSpan.FromMilliseconds(-1), new[] { "steam" }, "test");

        Assert.False(focus.IsActive);
        Assert.False(focus.Blocks("steam"));
    }
}
