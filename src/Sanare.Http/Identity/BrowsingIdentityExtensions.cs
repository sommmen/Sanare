using Sanare.Core.Acquisition;

namespace Sanare.Http.Identity;

/// <summary>
/// Projects a <see cref="BrowsingIdentity"/> onto the <see cref="RequestIdentity"/> shape that
/// <c>Sanare.Core</c> can carry on an <see cref="AcquisitionRequest"/>. <c>Sanare.Http</c> already
/// depends on <c>Sanare.Core</c>, so <see cref="AcquisitionRequest"/> cannot reference
/// <see cref="BrowsingIdentity"/> directly; this extension is the one place that bridges the two
/// shapes so callers never have to construct a <see cref="RequestIdentity"/> by hand
/// (docs/features/browsing-identity.md, T10).
/// </summary>
public static class BrowsingIdentityExtensions
{
    /// <summary>Projects this identity onto the wire-level shape <c>Sanare.Core</c> accepts.</summary>
    public static RequestIdentity ToRequestIdentity(this BrowsingIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return new RequestIdentity(identity.ProfileId, identity.Headers, identity.Cookies);
    }
}
