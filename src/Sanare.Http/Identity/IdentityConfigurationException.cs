namespace Sanare.Http.Identity;

/// <summary>
/// A fatal, startup-time identity configuration failure: either a profile fails a coherence rule
/// (<c>SNR-ID-001</c>) or a per-source override names an unknown profile (<c>SNR-ID-002</c>)
/// (docs/features/browsing-identity.md, "Error Handling"). Deliberately distinct from
/// <c>Sanare.Core.Acquisition.AcquisitionException</c>, which represents a request-time failure —
/// these are host-startup failures raised once, from <see cref="BrowsingIdentityProvider"/>'s
/// constructor, and are expected to prevent the host from starting at all.
/// </summary>
public sealed class IdentityConfigurationException(string code, string message) : Exception(message)
{
    /// <summary>The <c>SNR-ID-*</c> error code identifying the failure category.</summary>
    public string Code { get; } = code;
}
