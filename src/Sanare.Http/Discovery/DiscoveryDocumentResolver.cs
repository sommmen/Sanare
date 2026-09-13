using System.Text;
using Microsoft.Extensions.Logging;
using Sanare.Abstractions;
using Sanare.Core.Acquisition;
using Sanare.Http.Identity;
using Sanare.Http.Robots;

namespace Sanare.Http.Discovery;

/// <summary>A successfully fetched discovery document.</summary>
/// <param name="Url">The URL it was fetched from.</param>
/// <param name="Text">The decoded document text.</param>
public sealed record DiscoveryDocument(Uri Url, string Text);

/// <summary>
/// Resolves and fetches a host's <c>llms.txt</c> discovery document
/// (docs/features/acquisition-pipeline.md, "Key Behaviors" &gt; "Discovery documents (llms.txt)").
/// </summary>
/// <remarks>
/// <para>
/// Every failure here is non-fatal and surfaces as <c>SNR-ACQ-009</c> at warning severity. A discovery
/// document is evidence that makes authoring better, never a precondition for it; an absent or broken
/// <c>llms.txt</c> must not fail a run that would otherwise succeed. Consequently this path never
/// escalates a retry, never trips the breaker that guards page content, and never falls back to the
/// browser tier.
/// </para>
/// <para>
/// The fetch goes through the caller-supplied <see cref="IContentAcquirer"/> — the same identity, limiter,
/// cache, retry, and redaction as any other request. There is deliberately no separate network path,
/// because a second path would be a second place for governance to be forgotten.
/// </para>
/// </remarks>
public sealed class DiscoveryDocumentResolver(
    IContentAcquirer acquirer,
    IRobotsPolicy robots,
    ILogger<DiscoveryDocumentResolver>? logger = null)
{
    private const string PageRole = "discovery-llms";

    private readonly IContentAcquirer _acquirer = acquirer ?? throw new ArgumentNullException(nameof(acquirer));
    private readonly IRobotsPolicy _robots = robots ?? throw new ArgumentNullException(nameof(robots));

    /// <summary>The media types a discovery document may legitimately carry.</summary>
    public static IReadOnlySet<string> ExpectedContentTypes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "text/plain", "text/markdown", "text/x-markdown" };

    /// <summary>
    /// Picks the discovery-document URL a host advertises, or the conventional <c>/llms.txt</c> location.
    /// </summary>
    /// <param name="host">The host being resolved.</param>
    /// <param name="ruleSet">The parsed robots document whose references are searched first.</param>
    /// <returns>A same-host <c>https://</c> URL, or <see langword="null"/> when none resolves.</returns>
    public static Uri? Resolve(Uri host, RobotsRuleSet? ruleSet)
    {
        ArgumentNullException.ThrowIfNull(host);

        if (ruleSet is not null)
        {
            foreach (var reference in ruleSet.Sitemaps)
            {
                if (Uri.TryCreate(reference, UriKind.Absolute, out var candidate)
                    && IsUsable(host, candidate)
                    && candidate.AbsolutePath.EndsWith("llms.txt", StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
        }

        var conventional = new Uri($"https://{host.Host}/llms.txt");
        return IsUsable(host, conventional) ? conventional : null;
    }

    /// <summary>
    /// Fetches the discovery document for <paramref name="host"/>, returning <see langword="null"/> on any
    /// failure rather than throwing.
    /// </summary>
    /// <param name="host">The host to fetch for.</param>
    /// <param name="sourceId">The source the fetch is attributed to.</param>
    /// <param name="mode">The compliance posture the robots check is evaluated under.</param>
    /// <param name="ct">Cancels the fetch.</param>
    public async ValueTask<DiscoveryDocument?> FetchAsync(
        Uri host,
        string sourceId,
        AcquisitionMode mode = AcquisitionMode.Compliance,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        try
        {
            var ruleSet = await _robots.GetRuleSetAsync(host.Host, ct).ConfigureAwait(false);
            var url = Resolve(host, ruleSet);
            if (url is null)
            {
                return Warn(host, new AcquisitionException("SNR-ACQ-009", $"No discovery document is advertised for '{host.Host}'."));
            }

            var decision = await _robots.EvaluateAsync(url, mode, ct).ConfigureAwait(false);
            if (!decision.Allowed)
            {
                return Warn(host, new AcquisitionException("SNR-ACQ-009", $"robots.txt disallows the discovery document at '{url}'."));
            }

            var request = new AcquisitionRequest(
                url,
                sourceId,
                ExpectedContentTypes,
                PageRole,
                AcquisitionTier.Html);

            var content = await _acquirer.AcquireAsync(request, ct).ConfigureAwait(false);
            if (content.StatusCode is < 200 or >= 300)
            {
                return Warn(host, new AcquisitionException("SNR-ACQ-009", $"The discovery document at '{url}' returned HTTP {content.StatusCode}."));
            }

            var text = (content.Charset ?? Encoding.UTF8).GetString(content.Body.Span);
            return new DiscoveryDocument(content.FinalUrl, text);
        }
        catch (AcquisitionException exception)
        {
            return Warn(host, exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException)
        {
            return Warn(host, exception);
        }
    }

    private static bool IsUsable(Uri host, Uri candidate) =>
        candidate.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && string.Equals(candidate.Host, host.Host, StringComparison.OrdinalIgnoreCase);

    private DiscoveryDocument? Warn(Uri host, Exception exception)
    {
        logger?.LogWarning(
            exception,
            "SNR-ACQ-009: the discovery document for {Host} is unavailable; continuing without discovery evidence.",
            host.Host);
        return null;
    }
}
