using Sanare.Abstractions;
using Sanare.Core.Acquisition;

namespace Sanare.Http;

/// <summary>
/// Routes an acquisition to the transport approved for its requested tier.
/// </summary>
/// <remarks>
/// Browser acquisition is explicit: this dispatcher never retries a failed HTTP-tier acquisition in a
/// browser, and never falls back from browser to HTTP. Escalation belongs to approved-plan selection, not
/// the runtime transport boundary.
/// </remarks>
public sealed class TieredContentAcquirer(
    IContentAcquirer httpAcquirer,
    IContentAcquirer browserAcquirer) : IContentAcquirer
{
    private readonly IContentAcquirer _httpAcquirer = httpAcquirer ?? throw new ArgumentNullException(nameof(httpAcquirer));
    private readonly IContentAcquirer _browserAcquirer = browserAcquirer ?? throw new ArgumentNullException(nameof(browserAcquirer));

    /// <inheritdoc />
    public ValueTask<AcquiredContent> AcquireAsync(AcquisitionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Tier == AcquisitionTier.Browser
            ? _browserAcquirer.AcquireAsync(request, ct)
            : _httpAcquirer.AcquireAsync(request, ct);
    }
}
