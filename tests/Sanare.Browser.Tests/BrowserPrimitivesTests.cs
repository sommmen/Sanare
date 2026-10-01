using System.Globalization;
using Sanare.Abstractions;
using Sanare.Abstractions.Plans;
using Sanare.Browser;
using Sanare.Core.Acquisition;
using Sanare.Http.Identity;
using Sanare.Http.Identity.Profiles;

namespace Sanare.Browser.Tests;

public sealed class BrowserTierGateTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void EnsureAllowed_rejects_when_either_opt_in_is_false(bool globallyEnabled, bool sourceEnabled)
    {
        var gate = new BrowserTierGate();
        var policy = Policy(globallyEnabled, sourceEnabled);

        var exception = Assert.Throws<AcquisitionException>(() => gate.EnsureAllowed(policy));

        Assert.Equal("SNR-BRW-001", exception.Code);
    }

    [Fact]
    public void EnsureAllowed_permits_only_when_both_opt_ins_are_true() =>
        new BrowserTierGate().EnsureAllowed(Policy(globallyEnabled: true, sourceEnabled: true));

    private static ResolvedAcquisitionPolicy Policy(bool globallyEnabled, bool sourceEnabled) => new(
        new RateLimitOptions(), new RobotsOptions(), new RetryOptions(), new BreakerOptions(),
        new BrowserOptions(Enabled: globallyEnabled), sourceEnabled, RequiresImages: false);
}

public sealed class WaitStrategyParserTests
{
    [Theory]
    [InlineData(null, typeof(WaitStrategy.Load))]
    [InlineData("load", typeof(WaitStrategy.Load))]
    [InlineData("domcontentloaded", typeof(WaitStrategy.DomContentLoaded))]
    [InlineData("networkidle", typeof(WaitStrategy.NetworkIdle))]
    [InlineData("selector: main article", typeof(WaitStrategy.Selector))]
    [InlineData("function:documentReady", typeof(WaitStrategy.Function))]
    public void Parse_returns_only_supported_closed_union_cases(string? input, Type expectedType) =>
        Assert.IsType(expectedType, WaitStrategyParser.Parse(input));

    [Theory]
    [InlineData("selector:")]
    [InlineData("function:window.alert('unsafe')")]
    [InlineData("arbitrary javascript")]
    public void Parse_rejects_unrecognized_or_unsafe_values(string input) =>
        Assert.Throws<FormatException>(() => WaitStrategyParser.Parse(input));
}

public sealed class BrowserContextFactoryTests
{
    [Fact]
    public void BuildOptions_maps_identity_and_request_to_realistic_context_options()
    {
        var request = Request(timezoneId: "Europe/Amsterdam");
        var options = new BrowserContextFactory().BuildOptions(request, new DesktopChromeProfile(), AcquisitionMode.Compliance);

        Assert.Equal("nl-NL", options.Locale);
        Assert.Equal("Europe/Amsterdam", options.TimezoneId);
        Assert.NotNull(options.ViewportSize);
        Assert.Equal(1280, options.ViewportSize.Width);
        Assert.Equal(800, options.ViewportSize.Height);
        Assert.Equal(1, options.DeviceScaleFactor);
        Assert.True(options.JavaScriptEnabled);
        Assert.NotNull(options.UserAgent);
        Assert.Contains($"Chrome/{DesktopChromeProfile.ChromeMajorVersion}.", options.UserAgent, StringComparison.Ordinal);
        Assert.NotNull(options.ExtraHTTPHeaders);
        Assert.Contains(options.ExtraHTTPHeaders, header => header.Key == "Accept-Language" && header.Value.StartsWith("nl-NL", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildOptions_rejects_stealth_profile_without_implemented_capability()
    {
        var exception = Assert.Throws<AcquisitionException>(() =>
            new BrowserContextFactory().BuildOptions(Request(), new AssistantBrowserProfile(), AcquisitionMode.Stealth));

        Assert.Equal("SNR-BRW-001", exception.Code);
    }

    private static BrowserAcquisitionRequest Request(string timezoneId = "UTC") => new(
        new Uri("https://example.test/path"), "example", new AcquisitionSpec(AcquisitionMethod.Get, "/path", new Dictionary<string, string>(), null, []),
        CultureInfo.GetCultureInfo("nl-NL"), NavigationContext.TopLevel, TimezoneId: timezoneId);
}

public sealed class ResourceBlockerTests
{
    [Fact]
    public void ShouldBlock_disables_blocking_only_for_network_idle_sources_that_require_images()
    {
        Assert.False(ResourceBlocker.ShouldBlock(new WaitStrategy.NetworkIdle(), requiresImages: true));
        Assert.True(ResourceBlocker.ShouldBlock(new WaitStrategy.NetworkIdle(), requiresImages: false));
        Assert.True(ResourceBlocker.ShouldBlock(new WaitStrategy.Load(), requiresImages: true));
    }
}
