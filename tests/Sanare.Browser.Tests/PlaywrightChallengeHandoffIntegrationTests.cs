using System.Globalization;
using Microsoft.Playwright;
using Sanare.Core.Acquisition;
using Sanare.Http.Identity.Profiles;
using Sanare.Http.Resilience;

namespace Sanare.Browser.Tests;

[Collection("Browser")]
[Trait("Category", "Browser")]
public sealed class PlaywrightChallengeHandoffIntegrationTests(BrowserTestSiteFixture site) : IClassFixture<BrowserTestSiteFixture>
{
    [Fact]
    public async Task OpenAsync_in_live_mode_launches_without_automated_interactions()
    {
        using var playwright = await Playwright.CreateAsync();
        var confirmationCount = 0;
        var handoff = new PlaywrightChallengeHandoff(
            playwright,
            new BrowserContextFactory(),
            (_, _) => ValueTask.FromResult(new ChallengeHandoffSession(
                site.UriFor("lister-js.html"), new AssistantBrowserProfile(), CultureInfo.InvariantCulture)),
            _ =>
            {
                confirmationCount++;
                return ValueTask.FromResult(new ChallengeHandoffResult(true, "confirmed"));
            },
            new BrowserOptions(Enabled: true),
            ExecutionMode.Live);

        var result = await handoff.OpenAsync("browser-test");

        Assert.True(result.Resolved);
        Assert.Equal("confirmed", result.Reason);
        Assert.Equal(1, confirmationCount);
    }
}
