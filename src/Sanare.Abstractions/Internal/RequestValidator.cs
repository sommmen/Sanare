using System.Globalization;
using System.Text.RegularExpressions;
using Sanare.Abstractions.Diagnostics;

namespace Sanare.Abstractions.Internal;

/// <summary>
/// Result of <see cref="RequestValidator.Validate"/>: either a normalised request ready to execute, or a
/// terminal <see cref="ScrapeStatus"/> paired with the diagnostic explaining why validation failed.
/// Warning-level diagnostics (e.g. a <c>MaxItems</c> clamp) can accompany a successful outcome.
/// </summary>
public sealed record ValidationOutcome(
    ScrapeRequest? NormalizedRequest,
    ScrapeStatus? FailureStatus,
    IReadOnlyList<ScrapeDiagnostic> Diagnostics);

/// <summary>
/// Applies the normalisation rules from docs/features/scrape-api-contracts.md ("Request record and
/// normalisation") before a <see cref="ScrapeRequest"/> is executed. Failures are represented as data,
/// never thrown, with the sole exception of a null <paramref name="request"/> — a programming error.
/// </summary>
public static class RequestValidator
{
    private const int MaxUrlLength = 2048;
    private const int MaxSourceIdLength = 128;
    private const int MinMaxItems = 1;
    private const int MaxMaxItems = 1_000_000;

    private static readonly TimeSpan MaxFreshness = TimeSpan.FromDays(30);
    private static readonly Regex SourceIdPattern =
        new("^[a-z0-9-]+(/[a-z0-9-]+)*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Validates and normalises <paramref name="request"/>, applying rules 1 through 5 in order.</summary>
    public static ValidationOutcome Validate(ScrapeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var diagnostics = new List<ScrapeDiagnostic>();

        if (!IsValidUrl(request.Url))
        {
            diagnostics.Add(new ScrapeDiagnostic(
                "SNR-API-001", DiagnosticSeverity.Error,
                "Request URL must be an absolute http(s) URI without userinfo."));
            return new ValidationOutcome(null, ScrapeStatus.InvalidRequest, diagnostics);
        }

        string sourceId;
        if (request.SourceId is { } explicitSourceId)
        {
            if (explicitSourceId.Length > MaxSourceIdLength || !SourceIdPattern.IsMatch(explicitSourceId))
            {
                diagnostics.Add(new ScrapeDiagnostic(
                    "SNR-API-002", DiagnosticSeverity.Error, "Source id has an invalid format."));
                return new ValidationOutcome(null, ScrapeStatus.InvalidRequest, diagnostics);
            }

            sourceId = explicitSourceId;
        }
        else
        {
            sourceId = SourceIdDeriver.Derive(request.Url);
        }

        int? maxItems = request.MaxItems;
        if (maxItems is { } requestedMaxItems)
        {
            var clamped = Math.Clamp(requestedMaxItems, MinMaxItems, MaxMaxItems);
            if (clamped != requestedMaxItems)
            {
                diagnostics.Add(new ScrapeDiagnostic(
                    "SNR-API-003", DiagnosticSeverity.Warning,
                    "MaxItems was clamped to the supported range."));
            }

            maxItems = clamped;
        }

        if (request.Freshness is { } freshness && (freshness < TimeSpan.Zero || freshness > MaxFreshness))
        {
            diagnostics.Add(new ScrapeDiagnostic(
                "SNR-API-004", DiagnosticSeverity.Error,
                "Freshness must be between zero and 30 days."));
            return new ValidationOutcome(null, ScrapeStatus.InvalidRequest, diagnostics);
        }

        if (request.Culture is { } culture && !TryResolveCulture(culture))
        {
            diagnostics.Add(new ScrapeDiagnostic(
                "SNR-API-005", DiagnosticSeverity.Error, "Culture could not be resolved."));
            return new ValidationOutcome(null, ScrapeStatus.InvalidRequest, diagnostics);
        }

        var normalized = request with { SourceId = sourceId, MaxItems = maxItems };
        return new ValidationOutcome(normalized, null, diagnostics);
    }

    private static bool IsValidUrl(Uri url) =>
        url.IsAbsoluteUri
        && url.Scheme is "http" or "https"
        && !string.IsNullOrEmpty(url.Host)
        && string.IsNullOrEmpty(url.UserInfo)
        && url.OriginalString.Length <= MaxUrlLength;

    private static bool TryResolveCulture(string name)
    {
        try
        {
            var resolved = CultureInfo.GetCultureInfo(name);

            // ICU (used on Linux/macOS) resolves malformed names loosely instead of throwing
            // (e.g. "this-is-not-a-culture-name" silently becomes "this-IS"), unlike the Windows
            // NLS backend. Requiring the resolved name to round-trip keeps behaviour consistent
            // across platforms.
            return string.Equals(resolved.Name, name, StringComparison.OrdinalIgnoreCase);
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }
}

/// <summary>
/// Deterministically derives a <see cref="ScrapeRequest.SourceId"/> from a URL when the caller omits one:
/// <c>{host-slug}/{first-path-segment-slug}</c>, with <c>root</c> substituted for an empty path.
/// </summary>
public static class SourceIdDeriver
{
    /// <summary>Derives the source id for <paramref name="url"/>. The same URL always yields the same id.</summary>
    public static string Derive(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);

        var hostSlug = Slugify(url.Host);
        var segments = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var firstSegmentSlug = segments.Length > 0 ? Slugify(Uri.UnescapeDataString(segments[0])) : "root";
        return $"{hostSlug}/{firstSegmentSlug}";
    }

    private static string Slugify(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        var previousWasSeparator = false;
        foreach (var character in value.ToLowerInvariant())
        {
            if (character is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(character);
                previousWasSeparator = false;
            }
            else if (!previousWasSeparator && builder.Length > 0)
            {
                builder.Append('-');
                previousWasSeparator = true;
            }
        }

        var slug = builder.ToString().Trim('-');
        return slug.Length > 0 ? slug : "root";
    }
}
