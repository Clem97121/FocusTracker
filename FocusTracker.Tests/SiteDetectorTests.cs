using Xunit;
using FocusTracker.Domain;

namespace FocusTracker.Tests;

public class SiteDetectorTests
{
    [Theory]
    [InlineData("Lofi beats - YouTube - Google Chrome", "YouTube")]
    [InlineData("(3) YouTube — Mozilla Firefox", "YouTube")]
    [InlineData("Feed | LinkedIn - Personal - Microsoft Edge", "LinkedIn")]
    [InlineData("Clem97121/FocusTracker - GitHub - Google Chrome", "GitHub")]
    [InlineData("Inbox (12) - me@gmail.com - Gmail - Google Chrome", "Gmail")]
    [InlineData("DevOps Engineer - Djinni - Opera", "Djinni")]
    public void Detects_known_sites(string title, string site) =>
        Assert.Equal(site, SiteDetector.Detect(title));

    [Theory]
    [InlineData("New Tab - Google Chrome")]
    [InlineData("How to use YouTube for learning - My blog - Google Chrome")]
    [InlineData("")]
    public void Unknown_titles_stay_with_browser(string title) =>
        Assert.Null(SiteDetector.Detect(title));
}
