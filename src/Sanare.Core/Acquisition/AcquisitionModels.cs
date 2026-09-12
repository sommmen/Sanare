using System.Net;
using System.Text;
using Sanare.Abstractions;

namespace Sanare.Core.Acquisition;

public enum ContentOrigin { Network, Cache, Fixture, Browser }

/// <summary>
/// The wire-level shape of a composed browsing identity: an order-significant header list, cookie
/// contributions, and the profile id that produced them (docs/features/browsing-identity.md, T10).
/// Owned by <c>Sanare.Core</c> rather than <c>Sanare.Http</c> so <see cref="AcquisitionRequest"/> can
/// carry it without a circular project reference — <c>Sanare.Http</c> already depends on
/// <c>Sanare.Core</c>, so its richer <c>Sanare.Http.Identity.BrowsingIdentity</c> cannot appear here.
/// <c>Sanare.Http</c> projects a <c>BrowsingIdentity</c> into this shape before calling
/// <see cref="IContentAcquirer.AcquireAsync"/>.
/// </summary>
/// <param name="ProfileId">The identity profile that produced this identity, recorded on <see cref="AcquiredContent"/>.</param>
/// <param name="Headers">
/// The ordered header list to write onto the outgoing request, in list order. Never a dictionary —
/// header order is part of the identity contract and a dictionary cannot preserve it.
/// </param>
/// <param name="Cookies">Cookie contributions to send with the request, if any.</param>
public sealed record RequestIdentity(
    string ProfileId,
    IReadOnlyList<KeyValuePair<string, string>> Headers,
    IReadOnlyList<Cookie> Cookies);

public sealed record AcquisitionRequest(
    Uri Url,
    string SourceId,
    IReadOnlySet<string>? ExpectedContentTypes = null,
    string? PageRole = null,
    AcquisitionTier Tier = AcquisitionTier.Html,
    string Method = "GET",
    RequestIdentity? Identity = null)
{
    public IReadOnlySet<string> EffectiveExpectedContentTypes => ExpectedContentTypes ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "text/html", "application/xhtml+xml" };
}

public sealed record AcquiredContent(
    Uri RequestedUrl,
    Uri FinalUrl,
    int StatusCode,
    string ContentType,
    Encoding Charset,
    ReadOnlyMemory<byte> Body,
    IReadOnlyDictionary<string, string> Headers,
    ContentOrigin Origin,
    string? FixtureId,
    TimeSpan Elapsed,
    string? IdentityProfileId = null);

public sealed record AcquisitionOptions(
    bool Offline = false,
    bool AllowInsecureTransport = false,
    long MaximumResponseBytes = 16L * 1024 * 1024);

public sealed class AcquisitionException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public interface IContentAcquirer
{
    ValueTask<AcquiredContent> AcquireAsync(AcquisitionRequest request, CancellationToken ct = default);
}
