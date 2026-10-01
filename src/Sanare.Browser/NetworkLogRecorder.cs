using Microsoft.Playwright;
using Sanare.Core.Fixtures.Redaction;

namespace Sanare.Browser;

/// <summary>A single recorded JSON response, redacted before storage.</summary>
public sealed record NetworkLogEntry(string Method, string Url, int Status, long Bytes, string SampleOfBody);

/// <summary>The bounded set of entries recorded for one acquisition.</summary>
public sealed class BrowserNetworkLog
{
    private readonly List<NetworkLogEntry> _entries = [];
    private readonly Lock _sync = new();

    public IReadOnlyList<NetworkLogEntry> Entries
    {
        get { lock (_sync) { return _entries.ToArray(); } }
    }

    internal void Add(NetworkLogEntry entry)
    {
        lock (_sync) { _entries.Add(entry); }
    }
}

/// <summary>
/// Records JSON-content-type responses for authoring-time endpoint discovery
/// (docs/features/browser-tier.md, T9 "endpoint discovery"). Only active when
/// <see cref="BrowserAcquisitionRequest.CaptureNetwork"/> is <see langword="true"/>; body samples are
/// bounded and pass through the existing <see cref="IRedactor"/> before being stored.
/// </summary>
public sealed class NetworkLogRecorder
{
    private const int MaxSampleBytes = 4096;

    private readonly IRedactor _redactor;

    public NetworkLogRecorder(IRedactor? redactor = null) => _redactor = redactor ?? new Redactor();

    /// <summary>
    /// Attaches a response listener to <paramref name="context"/> that appends redacted JSON
    /// response samples to <paramref name="log"/> for as long as the returned subscription is held;
    /// dispose it (or let the context close) to stop recording.
    /// </summary>
    public IDisposable Attach(IBrowserContext context, BrowserNetworkLog log)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(log);

        async void Handler(object? sender, IResponse response)
        {
            try
            {
                await RecordAsync(response, log).ConfigureAwait(false);
            }
            catch
            {
                // Network log capture is a diagnostic, authoring-time aid; a single failed sample must
                // never fault the acquisition.
            }
        }

        context.Response += Handler;
        return new Subscription(() => context.Response -= Handler);
    }

    private async Task RecordAsync(IResponse response, BrowserNetworkLog log)
    {
        var headers = await response.AllHeadersAsync().ConfigureAwait(false);
        if (!headers.TryGetValue("content-type", out var contentType) || !contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var body = await response.BodyAsync().ConfigureAwait(false);
        var sampleLength = Math.Min(body.Length, MaxSampleBytes);
        var redacted = _redactor.Redact(body.AsSpan(0, sampleLength), headers);
        var sample = System.Text.Encoding.UTF8.GetString(redacted.Content);

        log.Add(new NetworkLogEntry(response.Request.Method, response.Url, response.Status, body.Length, sample));
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
