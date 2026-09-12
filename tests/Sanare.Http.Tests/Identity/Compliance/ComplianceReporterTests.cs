using System.Reflection;
using Sanare.Http.Identity;
using Sanare.Http.Identity.Compliance;

namespace Sanare.Http.Tests.Identity.Compliance;

public sealed class ComplianceReporterTests
{
    [Fact]
    public void GetReport_returns_an_empty_report_for_a_source_with_no_recorded_activity()
    {
        var reporter = new ComplianceReporter();

        var report = reporter.GetReport("unseen-source");

        Assert.Equal("unseen-source", report.SourceId);
        Assert.Equal(AcquisitionMode.Compliance, report.Mode);
        Assert.Equal(string.Empty, report.IdentityProfileId);
        Assert.Null(report.RobotsDecision);
        Assert.Equal(0, report.RequestCount);
        Assert.Empty(report.EnabledCapabilityIds);
        Assert.Null(report.ProxyProviderId);
    }

    [Fact]
    public void RecordResolution_sets_the_mode_and_identity_profile_for_a_source()
    {
        var reporter = new ComplianceReporter();

        reporter.RecordResolution("source-a", AcquisitionMode.Stealth, "DesktopChrome", ["capability-1"], "proxy-provider");
        var report = reporter.GetReport("source-a");

        Assert.Equal(AcquisitionMode.Stealth, report.Mode);
        Assert.Equal("DesktopChrome", report.IdentityProfileId);
        Assert.Equal(["capability-1"], report.EnabledCapabilityIds);
        Assert.Equal("proxy-provider", report.ProxyProviderId);
    }

    [Fact]
    public void RecordRobotsDecision_sets_the_most_recent_decision_for_a_source()
    {
        var reporter = new ComplianceReporter();

        reporter.RecordRobotsDecision("source-a", "Allowed");
        Assert.Equal("Allowed", reporter.GetReport("source-a").RobotsDecision);

        reporter.RecordRobotsDecision("source-a", "Disallowed");
        Assert.Equal("Disallowed", reporter.GetReport("source-a").RobotsDecision);
    }

    [Fact]
    public void RecordRequest_increments_the_request_count_for_a_source()
    {
        var reporter = new ComplianceReporter();

        reporter.RecordRequest("source-a");
        reporter.RecordRequest("source-a");
        reporter.RecordRequest("source-a");

        Assert.Equal(3, reporter.GetReport("source-a").RequestCount);
    }

    [Fact]
    public void Reports_for_different_sources_are_tracked_independently()
    {
        var reporter = new ComplianceReporter();

        reporter.RecordResolution("source-a", AcquisitionMode.Compliance, "AssistantBrowser");
        reporter.RecordResolution("source-b", AcquisitionMode.Stealth, "DesktopChrome");
        reporter.RecordRequest("source-a");

        Assert.Equal(1, reporter.GetReport("source-a").RequestCount);
        Assert.Equal(0, reporter.GetReport("source-b").RequestCount);
        Assert.Equal("AssistantBrowser", reporter.GetReport("source-a").IdentityProfileId);
        Assert.Equal("DesktopChrome", reporter.GetReport("source-b").IdentityProfileId);
    }

    /// <summary>
    /// AC-ID-015: the compliance report is safe to hand to <c>IScraperAdministration</c> because it
    /// has no credential-shaped member anywhere on its type — asserted via reflection rather than by
    /// convention, so a future addition of e.g. a proxy password field fails this test rather than
    /// silently shipping.
    /// </summary>
    [Fact]
    public void ComplianceReport_exposes_no_credential_shaped_member()
    {
        var forbiddenNameFragments = new[] { "password", "secret", "token", "apikey", "api_key", "credential", "auth" };

        var members = typeof(ComplianceReport).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var member in members)
        {
            var lowerName = member.Name.ToLowerInvariant();
            Assert.DoesNotContain(forbiddenNameFragments, fragment => lowerName.Contains(fragment, StringComparison.Ordinal));
        }
    }
}
