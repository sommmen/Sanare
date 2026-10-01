using Microsoft.Playwright;
using Sanare.Abstractions;
using Sanare.Core.Acquisition;
using Sanare.Http.Identity;
using Sanare.Http.Identity.Profiles;

namespace Sanare.Browser;

/// <summary>
/// Maps a governed identity onto Playwright's <see cref="BrowserNewContextOptions"/> so every
/// launched context is a coherent, unmasked deployment of the profile it claims to be
/// (docs/features/browser-tier.md, T6 "context realism"). Testable without launching a browser: the
/// produced options object is asserted directly against the profile's own composed headers.
/// </summary>
public sealed class BrowserContextFactory
{
    private const int ViewportWidth = 1280;
    private const int ViewportHeight = 800;

    /// <summary>
    /// Builds context options for <paramref name="request"/> under <paramref name="profile"/>. In
    /// <see cref="AcquisitionMode.Stealth"/>, only a profile whose declared capabilities are already
    /// implemented (Chromium lineage, matching the bundled engine's major version) is permitted; no
    /// runtime patching or fingerprint randomisation is ever applied.
    /// </summary>
    public BrowserNewContextOptions BuildOptions(BrowserAcquisitionRequest request, IdentityProfile profile, AcquisitionMode mode)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(profile);

        if (mode == AcquisitionMode.Stealth && !IsStealthCapable(profile))
        {
            throw new AcquisitionException(
                "SNR-BRW-001",
                $"Identity profile '{profile.ProfileId}' is not a validated browser-tier stealth capability.");
        }

        var identityRequest = new IdentityRequest(request.SourceId, request.TargetUri, request.Culture, request.Navigation, AcquisitionTier.Browser);
        var headers = profile.ComposeHeaders(identityRequest);
        var userAgent = FindHeader(headers, "User-Agent");
        var acceptLanguage = FindHeader(headers, "Accept-Language");

        var options = new BrowserNewContextOptions
        {
            Locale = request.Culture.Name,
            TimezoneId = request.TimezoneId,
            ViewportSize = new ViewportSize { Width = ViewportWidth, Height = ViewportHeight },
            DeviceScaleFactor = 1,
            JavaScriptEnabled = true,
        };

        if (userAgent is not null) options.UserAgent = userAgent;
        if (acceptLanguage is not null) options.ExtraHTTPHeaders = new[] { new KeyValuePair<string, string>("Accept-Language", acceptLanguage) };

        return options;
    }

    /// <summary>
    /// A profile is stealth-capable when it declares Chromium lineage matching the Chromium major
    /// version Playwright's bundled engine implements (<see cref="DesktopChromeProfile.ChromeMajorVersion"/>).
    /// </summary>
    public static bool IsStealthCapable(IdentityProfile profile) =>
        profile.IsChromiumLineage && profile.UserAgentMajorVersion == DesktopChromeProfile.ChromeMajorVersion;

    private static string? FindHeader(IReadOnlyList<KeyValuePair<string, string>> headers, string name)
    {
        foreach (var header in headers)
        {
            if (string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase)) return header.Value;
        }

        return null;
    }
}
