using System.Globalization;
using Sanare.Abstractions;

namespace Sanare.Http.Identity;

/// <summary>
/// Everything <see cref="IBrowsingIdentityProvider.GetIdentity"/> needs to compose a coherent
/// identity for one request (docs/features/browsing-identity.md, "Interfaces" &gt; "Inputs").
/// </summary>
/// <param name="SourceId">The source the request is issued on behalf of.</param>
/// <param name="TargetUri">The absolute URI about to be requested.</param>
/// <param name="Culture">Drives the composed <c>Accept-Language</c> header.</param>
/// <param name="Navigation">How this request relates to the run's navigation so far.</param>
/// <param name="Tier">The acquisition tier the identity is being composed for.</param>
/// <param name="ReferrerUri">
/// The URL the run actually visited immediately before this request, when known. Only ever
/// caller-supplied — there is no synthesis path that fabricates a referrer (coherence rule 5).
/// </param>
public sealed record IdentityRequest(
    string SourceId,
    Uri TargetUri,
    CultureInfo Culture,
    NavigationContext Navigation,
    AcquisitionTier Tier = AcquisitionTier.Html,
    Uri? ReferrerUri = null);
