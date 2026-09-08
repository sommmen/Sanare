using System.Diagnostics;
using Sanare.Abstractions.Telemetry;

namespace Sanare.Core.Observability;

/// <summary>Creates Sanare spans after sanitising all attributes.</summary>
public sealed class ScraperActivitySource
{
    private static readonly ActivitySource Source = new(ScraperTelemetry.ActivitySourceName);

    public bool HasListeners => Source.HasListeners();

    public Activity? StartRun(string sourceId, string planCommit, string? runId = null, string? tier = null, string? origin = null)
    {
        if (!Source.HasListeners())
        {
            return null;
        }

        var tags = new ActivityTagsCollection
        {
            { TagNames.SourceId, sourceId },
            { TagNames.PlanCommit, planCommit },
        };
        AddOptional(tags, TagNames.RunId, runId);
        AddOptional(tags, TagNames.Tier, tier);
        AddOptional(tags, TagNames.Origin, origin);
        return Source.StartActivity(SpanNames.RunExecute, ActivityKind.Internal, default(ActivityContext), tags);
    }

    public Activity? StartChild(string name, string sourceId, string planCommit, int? pageIndex = null, Uri? uri = null, bool enableSensitiveData = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Source.HasListeners())
        {
            return null;
        }

        var tags = new ActivityTagsCollection
        {
            { TagNames.SourceId, sourceId },
            { TagNames.PlanCommit, planCommit },
        };
        if (pageIndex.HasValue)
        {
            tags.Add(TagNames.PageIndex, pageIndex.Value);
        }

        if (uri is not null)
        {
            tags.Add(TagNames.UrlPath, enableSensitiveData ? uri.AbsoluteUri : uri.GetLeftPart(UriPartial.Path));
        }

        return Source.StartActivity(name, ActivityKind.Internal, default(ActivityContext), tags);
    }

    public static void SetFailure(Activity? activity, string errorCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        if (activity is null)
        {
            return;
        }

        activity.SetStatus(ActivityStatusCode.Error, errorCode);
        activity.SetTag(TagNames.ErrorType, errorCode);
    }

    private static void AddOptional(ActivityTagsCollection tags, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            tags.Add(key, value);
        }
    }
}
