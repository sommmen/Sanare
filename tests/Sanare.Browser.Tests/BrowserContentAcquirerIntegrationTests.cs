using System.Diagnostics;
using Microsoft.Playwright;
using Sanare.Abstractions.Plans;
using Sanare.Core.Acquisition;
using Sanare.Core.Observability;
using Sanare.Http.Identity.Consent;

namespace Sanare.Browser.Tests;

[Collection("Browser")]
[Trait("Category", "Browser")]
public sealed class BrowserContentAcquirerIntegrationTests(BrowserTestSiteFixture site) : IClassFixture<BrowserTestSiteFixture>
{
    [Theory]
    [InlineData("lister-js.html", "listing", 1)]
    [InlineData("product-js.html", "product-name", 1)]
    public async Task AcquireAsync_renders_javascript_content(string page, string marker, int expectedOccurrences)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var pool = new BrowserPool(playwright, new BrowserOptions(Enabled: true));
        using var client = new HttpClient();
        var acquirer = BrowserIntegrationHarness.CreateAcquirer(playwright, pool,
            BrowserIntegrationHarness.Options(new BrowserOptions(Enabled: true)), new RecordingFixtureCorpus(), client);

        var result = await acquirer.AcquireAsync(BrowserIntegrationHarness.Request(site.UriFor(page), $"selector:#{marker}"));

        Assert.Equal(200, result.StatusCode);
        Assert.Equal(expectedOccurrences, System.Text.Encoding.UTF8.GetString(result.Body.Span).Split($"id=\"{marker}\"", StringSplitOptions.None).Length - 1);
        Assert.Contains(page == "product-js.html" ? "Deterministic Widget" : "Charlie", System.Text.Encoding.UTF8.GetString(result.Body.Span), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AcquireAsync_emits_canonical_navigation_telemetry_when_given_a_plan_commit()
    {
        var (listener, activities) = CreateListener();
        using var playwright = await Playwright.CreateAsync();
        await using var pool = new BrowserPool(playwright, new BrowserOptions(Enabled: true));
        using var client = new HttpClient();
        var acquirer = BrowserIntegrationHarness.CreateAcquirer(playwright, pool,
            BrowserIntegrationHarness.Options(new BrowserOptions(Enabled: true)), new RecordingFixtureCorpus(), client);
        var request = BrowserIntegrationHarness.Request(new Uri(site.UriFor("lister-js.html") + "?token=secret")) with { PlanCommitId = "a1b2c3d4" };

        await acquirer.AcquireAsync(request);

        var activity = Assert.Single(activities);
        Assert.Equal(SpanNames.BrowserNavigate, activity.OperationName);
        Assert.Equal(Sanare.Abstractions.Telemetry.ScraperTelemetry.ActivitySourceName, activity.Source.Name);
        Assert.Equal("browser-test", activity.GetTagItem(TagNames.SourceId));
        Assert.Equal("a1b2c3d4", activity.GetTagItem(TagNames.PlanCommit));
        Assert.Equal("/lister-js.html", activity.GetTagItem(TagNames.UrlPath));
        Assert.Equal(site.UriFor("lister-js.html").Host, activity.GetTagItem(TagNames.Host));
        listener.Dispose();
    }

    [Fact]
    public async Task AcquireAsync_executes_click_and_scroll_interactions()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var pool = new BrowserPool(playwright, new BrowserOptions(Enabled: true, ScrollDelay: TimeSpan.FromMilliseconds(20)));
        using var client = new HttpClient();
        var options = BrowserIntegrationHarness.Options(new BrowserOptions(Enabled: true, ScrollDelay: TimeSpan.FromMilliseconds(20)));
        var acquirer = BrowserIntegrationHarness.CreateAcquirer(playwright, pool, options, new RecordingFixtureCorpus(), client);

        var consent = await acquirer.AcquireAsync(BrowserIntegrationHarness.Request(site.UriFor("consent-wall.html"), interactions: [new InteractionStep(PlanOperation.Click, ["#accept-consent"])]));
        var grid = await acquirer.AcquireAsync(BrowserIntegrationHarness.Request(site.UriFor("lister-infinite.html"), interactions: [new InteractionStep(PlanOperation.Scroll, ["3"])]));

        Assert.Contains("Reusable content.", System.Text.Encoding.UTF8.GetString(consent.Body.Span), StringComparison.Ordinal);
        Assert.Contains("Lazy item 18", System.Text.Encoding.UTF8.GetString(grid.Body.Span), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CookieBridge_harvests_and_seeds_consent_cookie()
    {
        using var playwright = await Playwright.CreateAsync();
        var jar = new HostCookieJar();
        var bridge = new CookieBridge(jar, new ConsentPolicy(), new BrowserStepExecutor());
        await using var browser = await playwright.Chromium.LaunchAsync();
        await using var source = await browser.NewContextAsync();
        var page = await source.NewPageAsync();
        await page.GotoAsync(site.UriFor("consent-wall.html").AbsoluteUri);
        await page.ClickAsync("#accept-consent");
        await bridge.HarvestAsync(source, site.BaseUri.Host);

        var harvested = Assert.Single(jar.Get(site.BaseUri.Host));
        Assert.Equal("sanare_consent", harvested.Name);
        Assert.Equal("accepted", harvested.Value);

        await using var target = await browser.NewContextAsync();
        await bridge.SeedAsync(target, site.BaseUri.Host);
        var seeded = await target.CookiesAsync([site.BaseUri.AbsoluteUri]);
        Assert.Contains(seeded, cookie => cookie.Name == "sanare_consent" && cookie.Value == "accepted");
    }

    [Fact]
    public async Task Resource_blocking_reduces_controlled_response_bytes()
    {
        var unblocked = await LoadAndMeasureAsync(false);
        var blocked = await LoadAndMeasureAsync(true);
        Assert.True(unblocked > blocked, $"Expected resource blocking to reduce bytes (unblocked={unblocked}, blocked={blocked}).");
    }

    private async Task<long> LoadAndMeasureAsync(bool blockResources)
    {
        site.ResetByteCount();
        using var playwright = await Playwright.CreateAsync();
        await using var pool = new BrowserPool(playwright, new BrowserOptions(Enabled: true, BlockResources: blockResources));
        using var client = new HttpClient();
        var options = BrowserIntegrationHarness.Options(new BrowserOptions(Enabled: true, BlockResources: blockResources));
        var acquirer = BrowserIntegrationHarness.CreateAcquirer(playwright, pool, options, new RecordingFixtureCorpus(), client);
        await acquirer.AcquireAsync(BrowserIntegrationHarness.Request(site.UriFor("lister-js.html"), "selector:#listing"));
        return site.BytesServed;
    }

    private static (ActivityListener Listener, List<Activity> Activities) CreateListener()
    {
        var activities = new List<Activity>();
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Sanare.Abstractions.Telemetry.ScraperTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Add(activity),
        };
        ActivitySource.AddActivityListener(listener);
        return (listener, activities);
    }
}