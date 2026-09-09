using System.Diagnostics;
using Sanare.Core.Observability;

namespace Sanare.Core.Tests.Observability;

public sealed class SpanHierarchyTests
{
    [Fact]
    public void StartRun_Returns_null_when_no_listener_is_attached()
    {
        // AC-OB-007: near-free instrumentation with no listener attached.
        var activitySource = new ScraperActivitySource();

        var activity = activitySource.StartRun("source-a", "commit-1");

        Assert.Null(activity);
    }

    [Fact]
    public void StartRun_Carries_source_id_and_plan_commit_on_the_root_span()
    {
        // AC-021: the root span carries source.id and plan.commit.
        using var listener = CreateListener();
        var activitySource = new ScraperActivitySource();

        using var activity = activitySource.StartRun("source-a", "commit-1", runId: "run-1", tier: "gold", origin: "cache");

        Assert.NotNull(activity);
        Assert.Equal(SpanNames.RunExecute, activity!.OperationName);
        Assert.Equal("source-a", activity.GetTagItem(TagNames.SourceId));
        Assert.Equal("commit-1", activity.GetTagItem(TagNames.PlanCommit));
        Assert.Equal("run-1", activity.GetTagItem(TagNames.RunId));
        Assert.Equal("gold", activity.GetTagItem(TagNames.Tier));
        Assert.Equal("cache", activity.GetTagItem(TagNames.Origin));
    }

    [Fact]
    public void StartRun_Omits_optional_tags_that_are_not_supplied()
    {
        using var listener = CreateListener();
        var activitySource = new ScraperActivitySource();

        using var activity = activitySource.StartRun("source-a", "commit-1");

        Assert.NotNull(activity);
        Assert.Null(activity!.GetTagItem(TagNames.RunId));
        Assert.Null(activity.GetTagItem(TagNames.Tier));
        Assert.Null(activity.GetTagItem(TagNames.Origin));
    }

    [Fact]
    public void StartChild_Creates_a_paginated_fetch_span_under_the_active_root()
    {
        // AC-OB-011: a paginated run creates one fetch child span per page under one root.
        using var listener = CreateListener();
        var activitySource = new ScraperActivitySource();

        using var root = activitySource.StartRun("source-a", "commit-1");
        var children = new List<Activity>();
        for (var pageIndex = 0; pageIndex < 5; pageIndex++)
        {
            using var child = activitySource.StartChild(SpanNames.AcquisitionFetch, "source-a", "commit-1", pageIndex: pageIndex);
            Assert.NotNull(child);
            children.Add(child!);
        }

        Assert.Equal(5, children.Count);
        Assert.All(children, child => Assert.Equal(root!.Id, child.ParentId));
        Assert.All(children, child => Assert.Equal(SpanNames.AcquisitionFetch, child.OperationName));
        Assert.Equal([0, 1, 2, 3, 4], children.Select(child => (int)child.GetTagItem(TagNames.PageIndex)!));
    }

    [Fact]
    public void StartChild_Does_not_tag_urls_to_prevent_cardinality_explosion()
    {
        // Per spec: URLs and dynamic values never become tags because they create unbounded cardinality.
        using var listener = CreateListener();
        var activitySource = new ScraperActivitySource();
        var uri = new Uri("https://example.test/products/1?session=abc123&token=secret");

        using var activity = activitySource.StartChild(SpanNames.BrowserNavigate, "source-a", "commit-1", uri: uri);

        Assert.NotNull(activity);
        Assert.Null(activity!.GetTagItem(TagNames.UrlPath));
    }

    [Fact]
    public void StartChild_Does_not_tag_urls_even_when_sensitive_data_is_enabled()
    {
        // Per spec: URLs should never become tags regardless of sensitive data flag, as they create unbounded cardinality.
        using var listener = CreateListener();
        var activitySource = new ScraperActivitySource();
        var uri = new Uri("https://example.test/products/1?session=abc123");

        using var activity = activitySource.StartChild(SpanNames.BrowserNavigate, "source-a", "commit-1", uri: uri, enableSensitiveData: true);

        Assert.NotNull(activity);
        Assert.Null(activity!.GetTagItem(TagNames.UrlPath));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void StartRun_Rejects_empty_or_whitespace_sourceId(string sourceId)
    {
        using var listener = CreateListener();
        var activitySource = new ScraperActivitySource();

        var exception = Assert.Throws<ArgumentException>(() => activitySource.StartRun(sourceId, "commit-1"));
        Assert.Equal("sourceId", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void StartRun_Rejects_empty_or_whitespace_planCommit(string planCommit)
    {
        using var listener = CreateListener();
        var activitySource = new ScraperActivitySource();

        var exception = Assert.Throws<ArgumentException>(() => activitySource.StartRun("source-a", planCommit));
        Assert.Equal("planCommit", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void StartChild_Rejects_empty_or_whitespace_sourceId(string sourceId)
    {
        using var listener = CreateListener();
        var activitySource = new ScraperActivitySource();

        var exception = Assert.Throws<ArgumentException>(() => activitySource.StartChild("span-name", sourceId, "commit-1"));
        Assert.Equal("sourceId", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void StartChild_Rejects_empty_or_whitespace_planCommit(string planCommit)
    {
        using var listener = CreateListener();
        var activitySource = new ScraperActivitySource();

        var exception = Assert.Throws<ArgumentException>(() => activitySource.StartChild("span-name", "source-a", planCommit));
        Assert.Equal("planCommit", exception.ParamName);
    }

    [Fact]
    public void SetFailure_Sets_error_status_with_the_SNR_code_as_error_type()
    {
        // AC-OB-003: a failed span has status Error with error.type set to the SNR-* code, not the message.
        using var listener = CreateListener();
        var activitySource = new ScraperActivitySource();
        using var activity = activitySource.StartRun("source-a", "commit-1");

        ScraperActivitySource.SetFailure(activity, "SNR-ACQ-002");

        Assert.NotNull(activity);
        Assert.Equal(ActivityStatusCode.Error, activity!.Status);
        Assert.Equal("SNR-ACQ-002", activity.GetTagItem(TagNames.ErrorType));
    }

    [Fact]
    public void SetFailure_Does_nothing_for_a_null_activity()
    {
        var exception = Record.Exception(() => ScraperActivitySource.SetFailure(null, "SNR-ACQ-002"));

        Assert.Null(exception);
    }

    [Fact]
    public void HasListeners_Reflects_whether_a_listener_is_currently_attached()
    {
        var activitySource = new ScraperActivitySource();

        Assert.False(activitySource.HasListeners);
        using (CreateListener())
        {
            Assert.True(activitySource.HasListeners);
        }
    }

    private static ActivityListener CreateListener()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Sanare.Abstractions.Telemetry.ScraperTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}
