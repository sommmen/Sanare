using System.Globalization;
using Microsoft.Playwright;
using Sanare.Abstractions.Plans;
using Sanare.Core.Acquisition;
using Sanare.Http.Identity;

namespace Sanare.Browser;

/// <summary>Input for a governed browser acquisition.</summary>
public sealed record BrowserAcquisitionRequest(
    Uri TargetUri,
    string SourceId,
    AcquisitionSpec Acquisition,
    CultureInfo Culture,
    NavigationContext Navigation,
    bool CaptureNetwork = false,
    string TimezoneId = "UTC");

/// <summary>Acquires rendered content using the browser tier.</summary>
public interface IBrowserContentAcquirer
{
    ValueTask<AcquiredContent> AcquireAsync(BrowserAcquisitionRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Enforces DR-004 before browser resources are allocated.</summary>
public sealed class BrowserTierGate
{
    public void EnsureAllowed(ResolvedAcquisitionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (!policy.Browser.Enabled || !policy.AllowBrowserTier)
        {
            throw new AcquisitionException("SNR-BRW-001", "Browser acquisition requires both global Browser.Enabled and per-source AllowBrowserTier.");
        }
    }
}

/// <summary>Lease over a pooled browser context.</summary>
public interface IBrowserLease : IAsyncDisposable
{
    IBrowserContext Context { get; }
    bool IsFaulted { get; }
    void MarkFaulted();
}

/// <summary>Bounded pool of browser contexts.</summary>
public interface IBrowserPool : IAsyncDisposable
{
    ValueTask<IBrowserLease> RentAsync(CancellationToken cancellationToken = default);
    ValueTask SweepIdleAsync(CancellationToken cancellationToken = default);
    BrowserPoolStatistics Statistics { get; }
}

/// <summary>Observable pool counters used for leak assertions and operational health.</summary>
public sealed record BrowserPoolStatistics(int Rented, int Available, int TotalCreated, int OpenPages);
