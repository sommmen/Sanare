using Sanare.Abstractions.Plans;
using Sanare.Http.Identity.Profiles;

namespace Sanare.Http.Identity;

/// <summary>
/// Everything <see cref="BrowsingIdentityProvider"/> needs to resolve identities
/// (docs/features/browsing-identity.md, "Inputs": "profile name, optional per-source overrides for
/// Accept-Language, consent-cookie policy"). The full, bindable configuration surface belongs to the
/// not-yet-implemented <c>hosting-configuration</c> package; this type is the narrower, code-level
/// shape <c>Sanare.Http</c> itself needs, so this package has no dependency on configuration binding.
/// </summary>
/// <param name="Profiles">Every profile the host may select from. Each is validated once, at provider construction.</param>
/// <param name="DefaultProfileId">The profile used for a source with no explicit override. Must name one of <paramref name="Profiles"/>.</param>
/// <param name="SupportedContentEncodings">The content-encodings the transport can actually decode (coherence rule 1).</param>
/// <param name="SourceOverrides">Per-source overrides, keyed by source id.</param>
public sealed record IdentityOptions(
    IReadOnlyList<IdentityProfile> Profiles,
    string DefaultProfileId,
    IReadOnlySet<string> SupportedContentEncodings,
    IReadOnlyDictionary<string, SourceIdentityOverride>? SourceOverrides = null);

/// <summary>
/// A single source's overrides within <see cref="IdentityOptions"/>.
/// </summary>
/// <param name="ProfileId">The identity profile to use for this source, or <see langword="null"/> to use the provider's default.</param>
/// <param name="Mode">The resolved acquisition mode for this source, recorded in the compliance report.</param>
/// <param name="ExpectedContentRootSelector">
/// A CSS selector for content expected to be present when the page is not consent-walled. Defaults to
/// <c>"body"</c>, which is deliberately permissive — a real deployment should supply the source's
/// actual content-root selector so consent detection is precise rather than tolerant of false negatives.
/// </param>
/// <param name="ConsentSpec">An optional per-source consent strategy override from the source's extraction plan.</param>
public sealed record SourceIdentityOverride(
    string? ProfileId = null,
    AcquisitionMode Mode = AcquisitionMode.Compliance,
    string ExpectedContentRootSelector = "body",
    ConsentSpec? ConsentSpec = null);
