using System.Text;
using Sanare.Abstractions;

namespace Sanare.Core.Acquisition;

public enum ContentOrigin { Network, Cache, Fixture, Browser }

public sealed record AcquisitionRequest(
    Uri Url,
    string SourceId,
    IReadOnlySet<string>? ExpectedContentTypes = null,
    string? PageRole = null,
    AcquisitionTier Tier = AcquisitionTier.Html,
    string Method = "GET")
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
    TimeSpan Elapsed);

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
