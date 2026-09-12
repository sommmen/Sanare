using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Sanare.Core.Fixtures;

namespace Sanare.Core.Acquisition;

/// <summary>Controlled HTTP acquisition boundary with deterministic fixture replay.</summary>
public sealed class HttpContentAcquirer(
    HttpClient client,
    IFixtureCorpus fixtures,
    AcquisitionOptions? options = null,
    TimeProvider? clock = null) : IContentAcquirer
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];
    private static readonly byte[] Utf16LeBom = [0xFF, 0xFE];
    private static readonly byte[] Utf16BeBom = [0xFE, 0xFF];

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
            throw new AcquisitionException("SNR-ACQ-001", "Only GET acquisition is currently supported.");
        }
        if (!_options.AllowInsecureTransport && !string.Equals(request.Url.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new AcquisitionException("SNR-ACQ-009", "Insecure HTTP transport is disabled.");
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
            throw new AcquisitionException("SNR-ACQ-009", "A redirect selected insecure HTTP transport.");
        }

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        var body = await ReadBoundedAsync(response.Content, ct).ConfigureAwait(false);
        var headers = ToHeaders(response);
        var fixture = await _fixtures.CaptureAsync(
            new CaptureRequest(request.Url.AbsoluteUri, request.SourceId, request.Tier, contentType,
                new MemoryStream(body, writable: false), headers, request.PageRole), ct).ConfigureAwait(false);
        ValidateContentType(contentType, request.EffectiveExpectedContentTypes);
        var charset = ResolveCharset(response.Content.Headers.ContentType, body);
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
        ValidateContentType(record.ContentType, request.EffectiveExpectedContentTypes);
        var fixture = await _fixtures.GetContentAsync(record.Id, ct).ConfigureAwait(false)
            ?? throw new AcquisitionException("SNR-FIX-001", "The matching fixture content is unavailable.");
        return new AcquiredContent(request.Url, request.Url, 200, fixture.ContentType, ResolveCharset(null, fixture.Bytes), fixture.Bytes,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), ContentOrigin.Fixture, record.Id, _clock.GetElapsedTime(started), request.Identity?.ProfileId);
    }
    private async ValueTask<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
        {
            if (output.Length + read > _options.MaximumResponseBytes)
            {
                throw new AcquisitionException("SNR-ACQ-007", "The response body exceeds the configured size ceiling.");
            }
            await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }
        return output.ToArray();
    }

    private static void ValidateContentType(string contentType, IReadOnlySet<string> expected)
    {
        if (!expected.Contains(contentType)) { throw new AcquisitionException("SNR-ACQ-006", $"Unexpected response content type '{contentType}'."); }
    }

    private static Encoding ResolveCharset(MediaTypeHeaderValue? contentType, ReadOnlySpan<byte> body)
    {
        if (TryGetEncoding(contentType?.CharSet, out var headerEncoding)) { return headerEncoding; }
        if (body.StartsWith(Utf8Bom)) { return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true); }
        if (body.StartsWith(Utf16LeBom)) { return Encoding.Unicode; }
        if (body.StartsWith(Utf16BeBom)) { return Encoding.BigEndianUnicode; }

        var sampleLength = Math.Min(body.Length, 8192);
        var sample = Encoding.ASCII.GetString(body[..sampleLength]);
        var match = Regex.Match(sample, "<meta\\s+[^>]*charset\\s*=\\s*[\\\"']?\\s*([^\\s\\\"'/>;]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            match = Regex.Match(sample, "<meta\\s+[^>]*content\\s*=\\s*[\\\"'][^>]*charset\\s*=\\s*([^\\s\\\"';>]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return TryGetEncoding(match.Success ? match.Groups[1].Value : null, out var metaEncoding)
            ? metaEncoding
            : new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    }

    private static bool TryGetEncoding(string? name, out Encoding encoding)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            try
            {
                encoding = Encoding.GetEncoding(name.Trim().Trim('\"', '\''));
                return true;
            }
            catch (ArgumentException) { }
        }

        encoding = null!;
        return false;
    }

    private static IReadOnlyDictionary<string, string> ToHeaders(HttpResponseMessage response) =>
        response.Headers.Concat(response.Content.Headers).ToDictionary(header => header.Key, header => string.Join(",", header.Value), StringComparer.OrdinalIgnoreCase);
}
