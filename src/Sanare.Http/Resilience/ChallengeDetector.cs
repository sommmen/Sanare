using System.Text;
using Sanare.Core.Acquisition;
using Sanare.Http.Identity.Consent;

namespace Sanare.Http.Resilience;

/// <summary>How strongly a response signals that the host is refusing automated access.</summary>
public enum ChallengeSeverity
{
    /// <summary>No challenge signature was recognised.</summary>
    None,

    /// <summary>
    /// A generic refusal — a bare <c>403</c>, or a CAPTCHA widget embedded in an otherwise normal page.
    /// Counts toward the block streak and opens a time-boxed circuit.
    /// </summary>
    Generic,

    /// <summary>
    /// A hard interstitial or IP-block page: the host has taken a deliberate, durable action against this
    /// client. Opens the circuit indefinitely and pauses the source rather than retrying on a timer.
    /// </summary>
    Hard,
}

/// <summary>
/// Recognises challenge and block interstitials by signature
/// (docs/features/acquisition-pipeline.md, "Key Behaviors" &gt; "Retry and circuit breaking").
/// </summary>
/// <remarks>
/// Detection only. This type never solves, submits, or works around a challenge (DR-006, NG-1–NG-3); it
/// exists so the pipeline can <em>stop</em> sooner and more precisely, not so it can push through. Page
/// classification is delegated to <see cref="WallClassifier"/> rather than duplicated, so a signature
/// added for consent handling is automatically honoured here too.
/// </remarks>
public sealed class ChallengeDetector
{
    private const int HeaderInspectionLimit = 64 * 1024;

    private static readonly string[] HardBodyMarkers =
    [
        "cf-challenge-running",
        "checking your browser before accessing",
        "attention required! | cloudflare",
        "ray id:",
        "access denied: your ip",
        "your ip address has been blocked",
        "请开启 javascript 并刷新",
        "ddos protection by",
        "just a moment...",
        "incapsula incident id",
        "request unsuccessful. incapsula",
        "pardon our interruption",
    ];

    private static readonly string[] HardHeaderNames =
    [
        "cf-mitigated",
        "x-datadome",
        "x-iinfo",
        "x-sucuri-block",
    ];

    private readonly WallClassifier _walls;

    /// <summary>Creates a detector delegating page classification to <paramref name="walls"/>.</summary>
    /// <param name="walls">The shared wall classifier. A fresh one is created when omitted.</param>
    public ChallengeDetector(WallClassifier? walls = null) => _walls = walls ?? new WallClassifier();

    /// <summary>
    /// Classifies <paramref name="content"/>. Bodies larger than 64 KiB are not inspected for markers: a
    /// real interstitial is a small page, and scanning a full 16 MiB document on every response would cost
    /// more than the signal is worth.
    /// </summary>
    /// <param name="content">The acquired response.</param>
    public ChallengeSeverity Classify(AcquiredContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return Classify(content.StatusCode, content.Headers, content.Body.Span, content);
    }

    /// <summary>
    /// Classifies a raw response, for callers holding bytes rather than an <see cref="AcquiredContent"/>.
    /// </summary>
    /// <param name="statusCode">The HTTP status observed.</param>
    /// <param name="headers">The response headers.</param>
    /// <param name="body">The response body.</param>
    public ChallengeSeverity Classify(int statusCode, IReadOnlyDictionary<string, string> headers, ReadOnlySpan<byte> body) =>
        Classify(statusCode, headers, body, content: null);

    private ChallengeSeverity Classify(int statusCode, IReadOnlyDictionary<string, string> headers, ReadOnlySpan<byte> body, AcquiredContent? content)
    {
        if (HasHardHeader(headers))
        {
            return ChallengeSeverity.Hard;
        }

        if (body.Length is > 0 and <= HeaderInspectionLimit)
        {
            var text = Encoding.UTF8.GetString(body);
            foreach (var marker in HardBodyMarkers)
            {
                if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    return ChallengeSeverity.Hard;
                }
            }

            if (content is not null && _walls.Classify(content) == WallClassification.Challenge)
            {
                return ChallengeSeverity.Generic;
            }
        }

        return statusCode == 403 ? ChallengeSeverity.Generic : ChallengeSeverity.None;
    }

    private static bool HasHardHeader(IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null)
        {
            return false;
        }

        foreach (var name in HardHeaderNames)
        {
            if (headers.ContainsKey(name))
            {
                return true;
            }
        }

        return false;
    }
}
