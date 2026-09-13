using System.Net;
using System.Text;
using Sanare.Abstractions;
using Sanare.Core.Fixtures;

namespace Sanare.Http.Tests.Acquisition;

/// <summary>
/// A <see cref="TimeProvider"/> whose clock only moves when a test moves it.
/// </summary>
/// <remarks>
/// <para>
/// Unlike the fakes elsewhere in this repository, this one overrides <see cref="CreateTimer"/>. That
/// matters because <c>Task.Delay(delay, timeProvider, ct)</c> schedules through a timer: a fake that only
/// overrides <see cref="GetUtcNow"/> and <see cref="GetTimestamp"/> leaves every delay running on the real
/// system clock, so a test asserting a 30-second backoff really would take 30 seconds.
/// </para>
/// <para>
/// Timers whose due time has elapsed fire synchronously inside <see cref="Advance"/>, which keeps the
/// tests deterministic without any polling or thread scheduling.
/// </para>
/// </remarks>
public sealed class FakeTimeProvider(DateTimeOffset? start = null) : TimeProvider
{
    private readonly List<FakeTimer> _timers = [];
    private readonly Lock _gate = new();
    private DateTimeOffset _now = start ?? new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <inheritdoc />
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <summary>The total time this clock has been advanced by.</summary>
    public TimeSpan TotalAdvanced { get; private set; }

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate) { return _now; }
    }

    /// <inheritdoc />
    public override long GetTimestamp()
    {
        lock (_gate) { return _now.UtcTicks; }
    }

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new FakeTimer(this, callback, state, dueTime, period, GetUtcNow());
        lock (_gate) { _timers.Add(timer); }
        return timer;
    }

    /// <summary>Moves the clock forward, firing any timer that comes due.</summary>
    /// <param name="delta">How far to advance.</param>
    public void Advance(TimeSpan delta)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delta, TimeSpan.Zero);

        List<FakeTimer> due;
        lock (_gate)
        {
            _now += delta;
            TotalAdvanced += delta;
            due = _timers.FindAll(timer => timer.IsDue(_now));
        }

        foreach (var timer in due)
        {
            timer.Fire(GetUtcNow());
        }
    }

    /// <summary>
    /// Advances the clock until every pending timer has fired, so an awaited delay completes without the
    /// test having to know the exact backoff the pipeline drew.
    /// </summary>
    /// <param name="limit">The furthest the clock may jump before giving up.</param>
    public void DrainTimers(TimeSpan? limit = null)
    {
        var ceiling = limit ?? TimeSpan.FromMinutes(10);
        var spent = TimeSpan.Zero;

        while (spent < ceiling)
        {
            DateTimeOffset? next;
            lock (_gate)
            {
                next = null;
                foreach (var timer in _timers)
                {
                    if (timer.DueUtc is { } dueUtc && (next is null || dueUtc < next))
                    {
                        next = dueUtc;
                    }
                }
            }

            if (next is null) { return; }

            var step = next.Value - GetUtcNow();
            if (step < TimeSpan.Zero) { step = TimeSpan.Zero; }
            Advance(step + TimeSpan.FromMilliseconds(1));
            spent += step;
        }
    }

    private void Remove(FakeTimer timer)
    {
        lock (_gate) { _timers.Remove(timer); }
    }

    private sealed class FakeTimer(
        FakeTimeProvider owner,
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period,
        DateTimeOffset createdUtc) : ITimer
    {
        public DateTimeOffset? DueUtc { get; private set; } =
            dueTime == Timeout.InfiniteTimeSpan ? null : createdUtc + dueTime;

        public bool IsDue(DateTimeOffset now) => DueUtc is { } due && now >= due;

        public void Fire(DateTimeOffset now)
        {
            if (!IsDue(now)) { return; }

            DueUtc = period == Timeout.InfiniteTimeSpan || period <= TimeSpan.Zero ? null : now + period;
            callback(state);
        }

        public bool Change(TimeSpan newDueTime, TimeSpan newPeriod)
        {
            DueUtc = newDueTime == Timeout.InfiniteTimeSpan ? null : owner.GetUtcNow() + newDueTime;
            return true;
        }

        public void Dispose() => owner.Remove(this);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>A scripted <see cref="HttpMessageHandler"/> that answers from a per-URL queue.</summary>
public sealed class ScriptedHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Queue<HttpResponseMessage>> _scripted = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<HttpRequestMessage> _requests = [];

    /// <summary>Every request the handler has seen, in order.</summary>
    public IReadOnlyList<HttpRequestMessage> Requests => _requests;

    /// <summary>How many requests the handler has seen.</summary>
    public int SendCount => _requests.Count;

    /// <summary>Queues a response for <paramref name="url"/>.</summary>
    /// <param name="url">The absolute URL to answer.</param>
    /// <param name="status">The status to return.</param>
    /// <param name="body">The body to return.</param>
    /// <param name="contentType">The media type of <paramref name="body"/>.</param>
    /// <param name="headers">Extra response headers, such as <c>Retry-After</c> or <c>Location</c>.</param>
    public ScriptedHandler Enqueue(
        string url,
        HttpStatusCode status = HttpStatusCode.OK,
        string body = "<html><body>ok</body></html>",
        string contentType = "text/html",
        params (string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, contentType),
        };

        foreach (var (name, value) in headers)
        {
            if (!response.Headers.TryAddWithoutValidation(name, value))
            {
                response.Content.Headers.TryAddWithoutValidation(name, value);
            }
        }

        if (!_scripted.TryGetValue(url, out var queue))
        {
            queue = new Queue<HttpResponseMessage>();
            _scripted[url] = queue;
        }

        queue.Enqueue(response);
        return this;
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _requests.Add(request);

        var url = request.RequestUri!.AbsoluteUri;
        if (_scripted.TryGetValue(url, out var queue) && queue.Count > 0)
        {
            return Task.FromResult(queue.Dequeue());
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent($"no scripted response for {url}", Encoding.UTF8, "text/plain"),
        });
    }
}

/// <summary>An in-memory corpus that records what the pipeline offered it.</summary>
public sealed class RecordingFixtureCorpus : IFixtureCorpus
{
    /// <summary>Every capture the pipeline offered, in order.</summary>
    public List<CaptureRequest> Captured { get; } = [];

    /// <inheritdoc />
    public ValueTask<FixtureRecord> CaptureAsync(CaptureRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Captured.Add(request);

        return ValueTask.FromResult(new FixtureRecord(
            Id: $"fixture-{Captured.Count:D4}",
            SourceId: request.SourceId,
            Url: request.Url,
            CapturedAt: DateTimeOffset.UtcNow,
            Tier: request.Tier,
            ContentType: request.ContentType,
            File: $"fixture-{Captured.Count:D4}.bin",
            Bytes: 0,
            ContentHash: string.Empty,
            NormalisedHash: string.Empty,
            Redactions: [],
            ReferencedByTags: [],
            PageRole: request.PageRole,
            Notes: null,
            RetentionTier: RetentionTier.Full,
            PinnedIssue: null));
    }

    /// <inheritdoc />
    public ValueTask<FixtureContent?> GetContentAsync(string fixtureId, CancellationToken ct = default) =>
        ValueTask.FromResult<FixtureContent?>(null);

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<FixtureRecord>> QueryAsync(FixtureQuery query, CancellationToken ct = default) =>
        ValueTask.FromResult<IReadOnlyList<FixtureRecord>>([]);

    /// <inheritdoc />
    public ValueTask<FixtureSlice> SliceAsync(string fixtureId, FixtureSliceRequest request, CancellationToken ct = default) =>
        ValueTask.FromResult(new FixtureSlice(fixtureId, string.Empty, 0, []));

    /// <inheritdoc />
    public ValueTask<PruneReport> PruneAsync(IReadOnlyCollection<string> protectedFixtureIds, CancellationToken ct = default) =>
        ValueTask.FromResult(new PruneReport([]));
}
