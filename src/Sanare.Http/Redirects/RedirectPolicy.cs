using Sanare.Core.Acquisition;

namespace Sanare.Http.Redirects;

/// <summary>One hop in a followed redirect chain.</summary>
/// <param name="From">The URL that issued the redirect.</param>
/// <param name="To">The URL it pointed at.</param>
/// <param name="StatusCode">The redirect status observed.</param>
/// <param name="CrossHost">Whether the hop left the previous host.</param>
public readonly record struct RedirectHop(Uri From, Uri To, int StatusCode, bool CrossHost);

/// <summary>
/// Walks redirect chains explicitly instead of letting <see cref="HttpClientHandler"/> follow them
/// (docs/features/acquisition-pipeline.md, "Key Behaviors" &gt; "Safety limits").
/// </summary>
/// <remarks>
/// <para>
/// The handler's built-in redirect following is disabled deliberately. A transparently-followed redirect
/// is invisible to governance: a cross-host hop would skip the destination host's robots check and its
/// rate limiter, and an <c>https → http</c> downgrade would silently happen inside the handler where no
/// policy can see it. Walking the chain by hand costs a few lines and makes each hop a governed decision.
/// </para>
/// <para>
/// The policy object itself is stateless; <see cref="ShouldFollow"/> is a pure function of the hop and
/// the chain length so far.
/// </para>
/// </remarks>
public sealed class RedirectPolicy(int maximumHops = 10, bool allowInsecureTransport = false)
{
    private readonly int _maximumHops = maximumHops > 0
        ? maximumHops
        : throw new ArgumentOutOfRangeException(nameof(maximumHops));

    /// <summary>The longest chain that will be followed before failing <c>SNR-ACQ-008</c>.</summary>
    public int MaximumHops => _maximumHops;

    /// <summary>Returns whether <paramref name="statusCode"/> is a redirect this policy walks.</summary>
    /// <param name="statusCode">The status to test.</param>
    public static bool IsRedirect(int statusCode) => statusCode is 301 or 302 or 303 or 307 or 308;

    /// <summary>
    /// Validates the next hop and returns it.
    /// </summary>
    /// <param name="from">The URL that issued the redirect.</param>
    /// <param name="location">The raw <c>Location</c> header, which may be relative.</param>
    /// <param name="statusCode">The redirect status observed.</param>
    /// <param name="hopsSoFar">How many hops the chain has already taken.</param>
    /// <exception cref="AcquisitionException">
    /// <c>SNR-ACQ-008</c> when the chain exceeds <see cref="MaximumHops"/>, <c>SNR-ACQ-013</c> when the hop
    /// downgrades to <c>http://</c>, or <c>SNR-ACQ-001</c> when <c>Location</c> is missing or unusable.
    /// </exception>
    public RedirectHop ShouldFollow(Uri from, string? location, int statusCode, int hopsSoFar)
    {
        ArgumentNullException.ThrowIfNull(from);

        if (hopsSoFar >= _maximumHops)
        {
            throw new AcquisitionException("SNR-ACQ-008", $"The redirect chain from '{from}' exceeded {_maximumHops} hops.");
        }

        if (string.IsNullOrWhiteSpace(location) || !Uri.TryCreate(from, location, out var target))
        {
            throw new AcquisitionException("SNR-ACQ-001", $"HTTP {statusCode} from '{from}' carried no usable Location header.");
        }

        if (!target.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) && !allowInsecureTransport)
        {
            throw new AcquisitionException("SNR-ACQ-013", $"The redirect from '{from}' downgrades to insecure '{target}'.");
        }

        var crossHost = !string.Equals(from.Host, target.Host, StringComparison.OrdinalIgnoreCase);
        return new RedirectHop(from, target, statusCode, crossHost);
    }
}
