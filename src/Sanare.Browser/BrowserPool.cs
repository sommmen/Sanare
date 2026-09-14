using System.Collections.Concurrent;
using Microsoft.Playwright;
using Sanare.Core.Acquisition;
using Sanare.Core.Observability;

namespace Sanare.Browser;

/// <summary>Bounded, lazily launched pool of isolated Chromium contexts.</summary>
public sealed class BrowserPool : IBrowserPool
{
    private readonly IPlaywright _playwright;
    private readonly BrowserOptions _options;
    private readonly TimeProvider _clock;
    private readonly ScraperMetrics? _metrics;
    private readonly SemaphoreSlim _capacity;
    private readonly SemaphoreSlim _sync = new(1, 1);
    private readonly Queue<ContextEntry> _available = [];
    private IBrowser? _browser;
    private bool _disposed;
    private int _rented;
    private int _created;
    private int _openPages;

    public BrowserPool(IPlaywright playwright, BrowserOptions options, TimeProvider? clock = null, ScraperMetrics? metrics = null)
    {
        _playwright = playwright ?? throw new ArgumentNullException(nameof(playwright));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? TimeProvider.System;
        _metrics = metrics;
        _capacity = new SemaphoreSlim(options.MaxContexts, options.MaxContexts);
    }

    public BrowserPoolStatistics Statistics => new(Volatile.Read(ref _rented), _capacity.CurrentCount, Volatile.Read(ref _created), Volatile.Read(ref _openPages));

    public async ValueTask<IBrowserLease> RentAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!await _capacity.WaitAsync(_options.EffectiveBrowserWaitTimeout, cancellationToken).ConfigureAwait(false))
        {
            throw new AcquisitionException("SNR-BRW-002", "Timed out waiting for an available browser context.");
        }

        try
        {
            await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                var now = _clock.GetUtcNow();
                ContextEntry? entry = null;
                while (_available.Count > 0 && entry is null)
                {
                    var candidate = _available.Dequeue();
                    if (ShouldRecycle(candidate, now))
                    {
                        await DisposeEntryAsync(candidate).ConfigureAwait(false);
                    }
                    else
                    {
                        entry = candidate;
                    }
                }

                entry ??= await CreateEntryAsync(cancellationToken).ConfigureAwait(false);
                entry.LastUsed = now;
                Interlocked.Increment(ref _rented);
                return new BrowserLease(this, entry);
            }
            finally { _sync.Release(); }
        }
        catch
        {
            _capacity.Release();
            throw;
        }
    }

    public async ValueTask SweepIdleAsync(CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = _clock.GetUtcNow();
            var retained = new Queue<ContextEntry>();
            while (_available.Count > 0)
            {
                var entry = _available.Dequeue();
                if (ShouldRecycle(entry, now)) await DisposeEntryAsync(entry).ConfigureAwait(false);
                else retained.Enqueue(entry);
            }
            while (retained.Count > 0) _available.Enqueue(retained.Dequeue());
            if (_available.Count == 0 && Volatile.Read(ref _rented) == 0) await CloseBrowserAsync().ConfigureAwait(false);
        }
        finally { _sync.Release(); }
    }

    internal async ValueTask ReturnAsync(ContextEntry entry, bool faulted)
    {
        await _sync.WaitAsync().ConfigureAwait(false);
        try
        {
            Interlocked.Decrement(ref _rented);
            entry.Operations++;
            entry.LastUsed = _clock.GetUtcNow();
            if (_disposed || faulted || ShouldRecycle(entry, entry.LastUsed)) await DisposeEntryAsync(entry).ConfigureAwait(false);
            else _available.Enqueue(entry);
            if (_available.Count == 0 && Volatile.Read(ref _rented) == 0) await CloseBrowserAsync().ConfigureAwait(false);
        }
        finally
        {
            _sync.Release();
            _capacity.Release();
        }
    }

    internal void PageOpened() => Interlocked.Increment(ref _openPages);
    internal void PageClosed() => Interlocked.Decrement(ref _openPages);

    private async ValueTask<ContextEntry> CreateEntryAsync(CancellationToken cancellationToken)
    {
        _browser ??= await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true }).ConfigureAwait(false);
        var context = await _browser.NewContextAsync().ConfigureAwait(false);
        Interlocked.Increment(ref _created);
        _metrics?.RecordBrowserContexts(1);
        var now = _clock.GetUtcNow();
        return new ContextEntry(context, now);
    }

    private bool ShouldRecycle(ContextEntry entry, DateTimeOffset now) =>
        entry.Operations >= _options.MaxOperationsPerContext ||
        now - entry.Created >= _options.EffectiveMaxContextAge ||
        now - entry.LastUsed >= _options.EffectiveContextIdleTimeout;

    private async ValueTask DisposeEntryAsync(ContextEntry entry)
    {
        await entry.Context.CloseAsync().ConfigureAwait(false);
        _metrics?.RecordBrowserContexts(-1);
    }

    private async ValueTask CloseBrowserAsync()
    {
        if (_browser is not null)
        {
            await _browser.CloseAsync().ConfigureAwait(false);
            _browser = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _sync.WaitAsync().ConfigureAwait(false);
        try
        {
            while (_available.Count > 0) await DisposeEntryAsync(_available.Dequeue()).ConfigureAwait(false);
            await CloseBrowserAsync().ConfigureAwait(false);
        }
        finally { _sync.Release(); _capacity.Dispose(); _sync.Dispose(); }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    internal sealed class ContextEntry(IBrowserContext context, DateTimeOffset created)
    {
        public IBrowserContext Context { get; } = context;
        public DateTimeOffset Created { get; } = created;
        public DateTimeOffset LastUsed { get; set; } = created;
        public int Operations { get; set; }
    }
}

/// <summary>Return-once wrapper around a pooled browser context.</summary>
public sealed class BrowserLease : IBrowserLease
{
    private readonly BrowserPool _pool;
    private BrowserPool.ContextEntry? _entry;

    internal BrowserLease(BrowserPool pool, BrowserPool.ContextEntry entry) { _pool = pool; _entry = entry; }
    public IBrowserContext Context => _entry?.Context ?? throw new ObjectDisposedException(nameof(BrowserLease));
    public bool IsFaulted { get; private set; }
    public void MarkFaulted() => IsFaulted = true;
    public async ValueTask DisposeAsync()
    {
        var entry = Interlocked.Exchange(ref _entry, null);
        if (entry is not null) await _pool.ReturnAsync(entry, IsFaulted).ConfigureAwait(false);
    }
}
