using System.Globalization;
using Microsoft.Playwright;
using Sanare.Abstractions;
using Sanare.Abstractions.Plans;
using Sanare.Core.Acquisition;
using Sanare.Core.Fixtures;
using Sanare.Http;
using Sanare.Http.Identity;
using Sanare.Http.Resilience;

namespace Sanare.Browser.Tests;

[CollectionDefinition("Browser", DisableParallelization = true)]
public sealed class BrowserTestCollection;

internal static class BrowserIntegrationHarness
{
    public static AcquisitionOptions Options(BrowserOptions browser) => new(
        Robots: new RobotsOptions(Enabled: false),
        Browser: browser,
        SourceOverrides: new Dictionary<string, AcquisitionPolicyOverride>
        {
            ["browser-test"] = new(AllowBrowserTier: true),
        });

    public static AcquisitionSpec Spec(string? waitFor = null, IReadOnlyList<InteractionStep>? interactions = null) => new(
        AcquisitionMethod.Get, "{url}", new Dictionary<string, string>(), waitFor, interactions ?? []);

    public static BrowserAcquisitionRequest Request(Uri uri, string? waitFor = null, IReadOnlyList<InteractionStep>? interactions = null, bool captureNetwork = false) => new(
        uri, "browser-test", Spec(waitFor, interactions), CultureInfo.GetCultureInfo("en-US"),
        NavigationContext.TopLevel, captureNetwork);

    public static BrowserContentAcquirer CreateAcquirer(
        IPlaywright playwright,
        BrowserPool pool,
        AcquisitionOptions options,
        IFixtureCorpus fixtures,
        HttpClient client,
        CookieBridge? cookies = null)
    {
        var governance = AcquisitionPipelineFactory.CreateGovernance(client, options);
        return new BrowserContentAcquirer(pool, new PageScope(pool), new BrowserStepExecutor(), fixtures, options,
            AcquisitionMode.Compliance, governance, challenges: new ChallengeDetector(), cookies: cookies);
    }
}