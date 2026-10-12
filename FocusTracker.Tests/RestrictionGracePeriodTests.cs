using Xunit;
using FocusTracker.Domain;

namespace FocusTracker.Tests;

public class RestrictionGracePeriodTests
{
    private static readonly DateTime Start = new(2026, 10, 12, 12, 0, 0);

    [Fact]
    public void First_violation_warns_then_waits_then_closes()
    {
        var grace = new RestrictionGracePeriod();

        Assert.Equal(RestrictionAction.Warn, grace.Check("chrome", true, Start));
        Assert.Equal(RestrictionAction.Wait, grace.Check("chrome", true, Start.AddSeconds(59)));
        Assert.Equal(RestrictionAction.Close, grace.Check("chrome", true, Start.AddSeconds(60)));
        Assert.Equal(RestrictionAction.Close, grace.Check("chrome", true, Start.AddMinutes(5)));
    }

    [Fact]
    public void No_violation_resets_warning()
    {
        var grace = new RestrictionGracePeriod();

        Assert.Equal(RestrictionAction.Warn, grace.Check("chrome", true, Start));
        Assert.Equal(RestrictionAction.None, grace.Check("chrome", false, Start.AddSeconds(30)));
        Assert.Equal(RestrictionAction.Warn, grace.Check("chrome", true, Start.AddSeconds(90)));
    }

    [Fact]
    public void Programs_are_tracked_separately()
    {
        var grace = new RestrictionGracePeriod();

        grace.Check("chrome", true, Start);
        Assert.Equal(RestrictionAction.Warn, grace.Check("steam", true, Start.AddSeconds(70)));
        Assert.Equal(RestrictionAction.Close, grace.Check("chrome", true, Start.AddSeconds(70)));
    }

    [Fact]
    public void Warning_from_previous_day_is_not_reused()
    {
        var grace = new RestrictionGracePeriod();

        grace.Check("chrome", true, Start);
        Assert.Equal(RestrictionAction.Warn, grace.Check("chrome", true, Start.AddDays(1)));
    }
}
