using System.Collections.Concurrent;
using System.Net;
using Sanare.Core.Acquisition;
using Sanare.Http.Identity.Compliance;
using Sanare.Http.Identity.Consent;
using Sanare.Http.Identity.Profiles;

namespace Sanare.Http.Identity;

/// <summary>
/// Default <see cref="IBrowsingIdentityProvider"/>. Identity is a pure function of
/// <c>(profile, culture, navigation context)</c> — there is no randomisation, per-request rotation,
/// or per-run seed (docs/features/browsing-identity.md, "Key Behaviors" &gt; "Stability"). Every
/// configured profile is validated once, at construction, against
/// <see cref="ProfileCoherenceValidator"/>; a failure there is what makes the host fail to start
/// (<c>SNR-ID-001</c>).
/// </summary>
public sealed class BrowsingIdentityProvider : IBrowsingIdentityProvider
{
    private readonly IReadOnlyDictionary<string, IdentityProfile> _profiles;
    private readonly string _defaultProfileId;
    private readonly IReadOnlyDictionary<string, SourceIdentityOverride> _sourceOverrides;
    private readonly IConsentPolicy _consentPolicy;
    private readonly HostCookieJar _cookieJar;
    private readonly ComplianceReporter _complianceReporter;
    private readonly ConcurrentDictionary<string, byte> _retriedKeys = new(StringComparer.Ordinal);

    /// <param name="options">Configured profiles, the default profile, and per-source overrides.</param>
    /// <param name="consentPolicy">The consent-wall detector to use. Defaults to <see cref="ConsentPolicy"/>.</param>
    /// <param name="cookieJar">The per-host cookie jar to use. Defaults to a new <see cref="HostCookieJar"/>.</param>
    /// <param name="complianceReporter">The compliance accumulator to use. Defaults to a new <see cref="ComplianceReporter"/>.</param>
    /// <exception cref="IdentityConfigurationException">
    /// <c>SNR-ID-001</c> when a profile fails a coherence rule; <c>SNR-ID-002</c> when the default
    /// profile or a source override names an unknown profile.
    /// </exception>
    public BrowsingIdentityProvider(
        IdentityOptions options,
        IConsentPolicy? consentPolicy = null,
        HostCookieJar? cookieJar = null,
        ComplianceReporter? complianceReporter = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DefaultProfileId);
        ArgumentNullException.ThrowIfNull(options.SupportedContentEncodings);

        var validator = new ProfileCoherenceValidator(options.SupportedContentEncodings);
        Dictionary<string, IdentityProfile> profilesById = new(StringComparer.Ordinal);
        foreach (var profile in options.Profiles)
        {
            var failures = validator.Validate(profile);
            if (failures.Count > 0)
            {
                throw new IdentityConfigurationException("SNR-ID-001",
                    $"Identity profile '{profile.ProfileId}' failed coherence validation: {string.Join("; ", failures)}");
            }

            profilesById[profile.ProfileId] = profile;
        }

        if (!profilesById.ContainsKey(options.DefaultProfileId))
        {
            throw new IdentityConfigurationException("SNR-ID-002",
                $"Default identity profile '{options.DefaultProfileId}' is not among the configured profiles.");
        }

        _sourceOverrides = options.SourceOverrides ?? new Dictionary<string, SourceIdentityOverride>(StringComparer.Ordinal);
        foreach (var (sourceId, sourceOverride) in _sourceOverrides)
        {
            if (sourceOverride.ProfileId is { Length: > 0 } profileId && !profilesById.ContainsKey(profileId))
            {
                throw new IdentityConfigurationException("SNR-ID-002",
                    $"Source '{sourceId}' references unknown identity profile '{profileId}'.");
            }
        }

        _profiles = profilesById;
        _defaultProfileId = options.DefaultProfileId;
        _consentPolicy = consentPolicy ?? new ConsentPolicy();
        _cookieJar = cookieJar ?? new HostCookieJar();
        _complianceReporter = complianceReporter ?? new ComplianceReporter();

        foreach (var (sourceId, sourceOverride) in _sourceOverrides)
        {
            _complianceReporter.RecordResolution(sourceId, sourceOverride.Mode, sourceOverride.ProfileId ?? _defaultProfileId);
        }
    }

    public BrowsingIdentity GetIdentity(IdentityRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var profile = ResolveProfile(request.SourceId);
        var headers = profile.ComposeHeaders(request);
        var cookies = _cookieJar.Get(request.TargetUri.Host);
        _complianceReporter.RecordRequest(request.SourceId);
        return new BrowsingIdentity(profile.ProfileId, headers, cookies);
    }

    public ConsentDecision EvaluateConsent(AcquiredContent content, string sourceId)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        var sourceOverride = ResolveSourceOverride(sourceId);
        var retryKey = $"{sourceId}|{content.FinalUrl.AbsoluteUri}";
        var alreadyRetried = _retriedKeys.ContainsKey(retryKey);
        var decision = _consentPolicy.Evaluate(content, sourceOverride.ExpectedContentRootSelector, sourceOverride.ConsentSpec, alreadyRetried);

        if (!decision.WallDetected)
        {
            _retriedKeys.TryRemove(retryKey, out _);
            return decision;
        }

        if (decision.CookieName is { Length: > 0 } cookieName && decision.CookieValue is { Length: > 0 } cookieValue)
        {
            _cookieJar.Set(content.FinalUrl.Host, new Cookie(cookieName, cookieValue, "/", content.FinalUrl.Host));
        }

        _retriedKeys[retryKey] = 0;
        return decision;
    }

    public ComplianceReport GetComplianceReport(string sourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        return _complianceReporter.GetReport(sourceId);
    }

    private IdentityProfile ResolveProfile(string sourceId)
    {
        var profileId = _sourceOverrides.TryGetValue(sourceId, out var sourceOverride) && sourceOverride.ProfileId is { Length: > 0 } overrideId
            ? overrideId
            : _defaultProfileId;
        if (!_profiles.TryGetValue(profileId, out var profile))
        {
            throw new IdentityConfigurationException("SNR-ID-002", $"Source '{sourceId}' references unknown identity profile '{profileId}'.");
        }

        return profile;
    }

    private SourceIdentityOverride ResolveSourceOverride(string sourceId) =>
        _sourceOverrides.TryGetValue(sourceId, out var sourceOverride) ? sourceOverride : new SourceIdentityOverride();
}
