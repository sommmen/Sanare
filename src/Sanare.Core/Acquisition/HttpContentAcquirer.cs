using Sanare.Core.Acquisition.Content;
using Sanare.Core.Fixtures;

namespace Sanare.Core.Acquisition;

/// <summary>
/// Controlled HTTP acquisition boundary with deterministic fixture replay. This type is a
/// <em>transport</em>: it owns guards, the send, the bounded read, fixture capture, and replay. Every
/// governance concern — limiters, robots, caching, retries, breakers — lives in
/// <c>Sanare.Http.GovernedContentAcquirer</c>, which decorates this type
/// (docs/features/acquisition-pipeline.md, "File Structure").
/// </summary>
public sealed class HttpContentAcquirer(
    HttpClient client,
    IFixtureCorpus fixtures,
    AcquisitionOptions? options = null,
    TimeProvider? clock = null) : IContentAcquirer
{
    private readonly HttpClient _client = client ?? throw new ArgumentNullException(nameof(client));
    private readonly IFixtureCorpus _fixtures = fixtures ?? throw new ArgumentNullException(nameof(fixtures));
    private readonly AcquisitionOptions _options = options ?? new AcquisitionOptions();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async ValueTask<AcquiredContent> AcquireAsync(AcquisitionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourceId);
        if (!request.Url.IsAbsoluteUri) { throw new ArgumentException("The acquisition URL must be absolute.", nameof(request)); }
        if (!string.Equals(request.Method, HttpMethod.Get.Method, StringComparison.OrdinalIgnoreCase))
        {
            throw new AcquisitionException("SNR-ACQ-012", "Only GET acquisition is currently supported.");
        }
        if (!_options.AllowInsecureTransport && !string.Equals(request.Url.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new AcquisitionException("SNR-API-001", "Insecure HTTP transport is disabled.");
        }

        var started = _clock.GetTimestamp();
        if (_options.Offline)
        {
            return await ReplayAsync(request, started, ct).ConfigureAwait(false);
        }

        using var message = new HttpRequestMessage(HttpMethod.Get, request.Url);
        ApplyIdentity(message, request.Identity);
        using var response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        var finalUrl = response.RequestMessage?.RequestUri ?? request.Url;
        if (!_options.AllowInsecureTransport && !string.Equals(finalUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new AcquisitionException("SNR-ACQ-013", "A redirect selected insecure HTTP transport.");
        }

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        var body = await ReadBoundedAsync(response.Content, ct).ConfigureAwait(false);
        var headers = ToHeaders(response);
        var fixture = await _fixtures.CaptureAsync(
            new CaptureRequest(request.Url.AbsoluteUri, request.SourceId, request.Tier, contentType,
                new MemoryStream(body, writable: false), headers, request.PageRole), ct).ConfigureAwait(false);
        ContentTypeGate.Validate(contentType, request.EffectiveExpectedContentTypes);
        var charset = CharsetDetector.Resolve(response.Content.Headers.ContentType?.CharSet, body).Encoding;
        return new AcquiredContent(request.Url, finalUrl, (int)response.StatusCode, contentType, charset, body, headers, ContentOrigin.Network, fixture?.Id, _clock.GetElapsedTime(started), request.Identity?.ProfileId);
    }

    /// <summary>
    /// Writes an identity's headers onto <paramref name="message"/> in list order, plus a
    /// <c>Cookie</c> header assembled from its cookie contributions. When <paramref name="identity"/>
    /// is <see langword="null"/> this is a no-op, so the no-identity path stays byte-identical to
    /// behaviour before identity support existed.
    /// </summary>
    private static void ApplyIdentity(HttpRequestMessage message, RequestIdentity? identity)
    {
        if (identity is null) { return; }

        foreach (var (name, value) in identity.Headers)
        {
            message.Headers.TryAddWithoutValidation(name, value);
        }

        if (identity.Cookies.Count > 0)
        {
            var cookieHeader = string.Join("; ", identity.Cookies.Select(cookie => $"{cookie.Name}={cookie.Value}"));
            message.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
        }
    }

    private async ValueTask<AcquiredContent> ReplayAsync(AcquisitionRequest request, long started, CancellationToken ct)
    {
        var records = await _fixtures.QueryAsync(new FixtureQuery(SourceId: request.SourceId, Url: request.Url.AbsoluteUri, PageRole: request.PageRole, Latest: true), ct).ConfigureAwait(false);
        var record = records.FirstOrDefault();
        if (record is null) { throw new AcquisitionException("SNR-FIX-001", "No matching fixture is available while offline."); }
        ContentTypeGate.Validate(record.ContentType, request.EffectiveExpectedContentTypes);
        var fixture = await _fixtures.GetContentAsync(record.Id, ct).ConfigureAwait(false)
            ?? throw new AcquisitionException("SNR-FIX-001", "The matching fixture content is unavailable.");
        return new AcquiredContent(request.Url, request.Url, 200, fixture.ContentType, CharsetDetector.Resolve(headerCharset: null, fixture.Bytes).Encoding, fixture.Bytes,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), ContentOrigin.Fixture, record.Id, _clock.GetElapsedTime(started), request.Identity?.ProfileId);
    }

    private async ValueTask<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await BoundedStreamReader.ReadAsync(stream, _options.MaximumResponseBytes, "SNR-ACQ-007", ct).ConfigureAwait(false);
    }

    private static IReadOnlyDictionary<string, string> ToHeaders(HttpResponseMessage response) =>
        response.Headers.Concat(response.Content.Headers).ToDictionary(header => header.Key, header => string.Join(",", header.Value), StringComparer.OrdinalIgnoreCase);
}
