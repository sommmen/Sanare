using Sanare.Core.Acquisition;

namespace Sanare.Core.Tests.Acquisition;

/// <summary>
/// Covers the browser tier governance surface added for docs/features/browser-tier.md, "Task order"
/// T1: <see cref="BrowserOptions"/> defaults/validation and the DR-004 double opt-in layering in
/// <see cref="AcquisitionOptions.ResolveFor"/>.
/// </summary>
public sealed class AcquisitionPolicyOptionsTests
{
    [Fact]
    public void BrowserOptions_defaults_match_the_documented_values()
    {
        var options = new BrowserOptions();

        Assert.False(options.Enabled);
        Assert.Equal(2, options.MaxContexts);
        Assert.Equal(TimeSpan.FromSeconds(30), options.EffectiveBrowserWaitTimeout);
        Assert.Equal(50, options.MaxOperationsPerContext);
        Assert.Equal(TimeSpan.FromMinutes(15), options.EffectiveMaxContextAge);
        Assert.Equal(TimeSpan.FromMinutes(2), options.EffectiveContextIdleTimeout);
        Assert.Equal(50, options.MaxScrolls);
        Assert.Equal(TimeSpan.FromMilliseconds(250), options.EffectiveScrollDelay);
        Assert.True(options.BlockResources);
        Assert.False(options.CaptureNetwork);
        Assert.NotEmpty(options.EffectiveBlockedHosts);
    }

    [Fact]
    public void BrowserOptions_rejects_a_non_positive_wait_timeout()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BrowserOptions(BrowserWaitTimeout: TimeSpan.Zero));
    }

    [Fact]
    public void BrowserOptions_rejects_a_non_positive_max_context_age()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BrowserOptions(MaxContextAge: TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void BrowserOptions_rejects_a_non_positive_context_idle_timeout()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BrowserOptions(ContextIdleTimeout: TimeSpan.Zero));
    }

    [Fact]
    public void BrowserOptions_rejects_a_negative_scroll_delay()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BrowserOptions(ScrollDelay: TimeSpan.FromMilliseconds(-1)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void BrowserOptions_rejects_a_non_positive_max_contexts(int maxContexts)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BrowserOptions(MaxContexts: maxContexts));
    }

    [Fact]
    public void BrowserOptions_honours_a_custom_blocked_host_list()
    {
        var options = new BrowserOptions(BlockedHosts: ["example-ads.test"]);

        Assert.Equal(["example-ads.test"], options.EffectiveBlockedHosts);
    }

    [Fact]
    public void AcquisitionOptions_disables_the_browser_tier_by_default()
    {
        var options = new AcquisitionOptions();

        Assert.False(options.EffectiveBrowser.Enabled);
    }

    [Fact]
    public void ResolveFor_denies_the_browser_tier_when_only_the_global_flag_is_enabled()
    {
        // DR-004 double opt-in: the global Browser.Enabled flag alone must never be sufficient.
        var options = new AcquisitionOptions(Browser: new BrowserOptions(Enabled: true));

        var resolved = options.ResolveFor("example.test", sourceId: "some-source");

        Assert.True(resolved.Browser.Enabled);
        Assert.False(resolved.AllowBrowserTier);
    }

    [Fact]
    public void ResolveFor_denies_the_browser_tier_when_only_the_per_source_flag_is_enabled()
    {
        var options = new AcquisitionOptions(
            SourceOverrides: new Dictionary<string, AcquisitionPolicyOverride>
            {
                ["some-source"] = new AcquisitionPolicyOverride(AllowBrowserTier: true),
            });

        var resolved = options.ResolveFor("example.test", sourceId: "some-source");

        Assert.False(resolved.Browser.Enabled);
        Assert.True(resolved.AllowBrowserTier);
    }

    [Fact]
    public void ResolveFor_allows_the_browser_tier_only_once_both_opt_ins_are_set()
    {
        var options = new AcquisitionOptions(
            Browser: new BrowserOptions(Enabled: true),
            SourceOverrides: new Dictionary<string, AcquisitionPolicyOverride>
            {
                ["some-source"] = new AcquisitionPolicyOverride(AllowBrowserTier: true),
            });

        var resolved = options.ResolveFor("example.test", sourceId: "some-source");

        Assert.True(resolved.Browser.Enabled);
        Assert.True(resolved.AllowBrowserTier);
    }

    [Fact]
    public void ResolveFor_layers_the_source_override_over_the_host_override_for_browser_flags()
    {
        var options = new AcquisitionOptions(
            HostOverrides: new Dictionary<string, AcquisitionPolicyOverride>
            {
                ["example.test"] = new AcquisitionPolicyOverride(AllowBrowserTier: true, RequiresImages: true),
            },
            SourceOverrides: new Dictionary<string, AcquisitionPolicyOverride>
            {
                ["some-source"] = new AcquisitionPolicyOverride(AllowBrowserTier: false, RequiresImages: false),
            });

        var resolved = options.ResolveFor("example.test", sourceId: "some-source");

        // The source override is more specific and wins even though its values are the non-default false.
        Assert.False(resolved.AllowBrowserTier);
        Assert.False(resolved.RequiresImages);
    }

    [Fact]
    public void ResolveFor_falls_back_to_the_host_override_when_no_source_override_exists()
    {
        var options = new AcquisitionOptions(
            HostOverrides: new Dictionary<string, AcquisitionPolicyOverride>
            {
                ["example.test"] = new AcquisitionPolicyOverride(AllowBrowserTier: true, RequiresImages: true),
            });

        var resolved = options.ResolveFor("example.test", sourceId: "some-source");

        Assert.True(resolved.AllowBrowserTier);
        Assert.True(resolved.RequiresImages);
    }

    [Fact]
    public void ResolveFor_defaults_browser_flags_to_false_with_no_overrides()
    {
        var options = new AcquisitionOptions();

        var resolved = options.ResolveFor("example.test");

        Assert.False(resolved.AllowBrowserTier);
        Assert.False(resolved.RequiresImages);
    }
}
