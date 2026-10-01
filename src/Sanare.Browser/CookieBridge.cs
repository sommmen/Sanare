using Microsoft.Playwright;
using Sanare.Abstractions.Plans;
using Sanare.Core.Acquisition;
using Sanare.Http.Identity.Consent;
using NetCookie = System.Net.Cookie;
using PwCookie = Microsoft.Playwright.Cookie;

namespace Sanare.Browser;

/// <summary>
/// Lifts cookies between a Playwright context and the shared <see cref="HostCookieJar"/>
/// (docs/features/browser-tier.md, T10 "cookie bridge"). Consent-wall detection reuses
/// <see cref="IConsentPolicy"/>/<c>WallClassifier</c>; a single accept-click, when required, is
/// issued through <see cref="IBrowserStepExecutor"/> rather than a bespoke Playwright call.
/// </summary>
public sealed class CookieBridge(HostCookieJar jar, IConsentPolicy consentPolicy, IBrowserStepExecutor stepExecutor)
{
    private readonly HostCookieJar _jar = jar ?? throw new ArgumentNullException(nameof(jar));
    private readonly IConsentPolicy _consentPolicy = consentPolicy ?? throw new ArgumentNullException(nameof(consentPolicy));
    private readonly IBrowserStepExecutor _stepExecutor = stepExecutor ?? throw new ArgumentNullException(nameof(stepExecutor));

    /// <summary>Seeds the context with every jar cookie held for <paramref name="host"/>, before navigation.</summary>
    public async Task SeedAsync(IBrowserContext context, string host, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        cancellationToken.ThrowIfCancellationRequested();

        var cookies = _jar.Get(host);
        if (cookies.Count == 0) return;

        var playwrightCookies = new List<PwCookie>(cookies.Count);
        foreach (var cookie in cookies)
        {
            playwrightCookies.Add(ToPlaywrightCookie(cookie, host));
        }

        await context.AddCookiesAsync(playwrightCookies).ConfigureAwait(false);
    }

    /// <summary>Lifts every cookie currently in the context back into the jar, keyed by <paramref name="host"/>.</summary>
    public async Task HarvestAsync(IBrowserContext context, string host, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        cancellationToken.ThrowIfCancellationRequested();

        var cookies = await context.CookiesAsync().ConfigureAwait(false);
        foreach (var cookie in cookies)
        {
            _jar.Set(host, ToNetCookie(cookie));
        }
    }

    /// <summary>
    /// Evaluates <paramref name="content"/> for a consent wall and, when a signature is found and a
    /// cookie response is prescribed, persists that cookie to the jar and (when a click selector is
    /// configured) runs a single <see cref="PlanOperation.Click"/> step through
    /// <see cref="IBrowserStepExecutor"/> to dismiss it.
    /// </summary>
    public async Task<ConsentDecision> ResolveConsentAsync(
        IPage page,
        AcquiredContent content,
        string host,
        string expectedContentRootSelector,
        ConsentSpec? consentSpec,
        bool retryAttempted,
        BrowserOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentNullException.ThrowIfNull(options);

        var decision = _consentPolicy.Evaluate(content, expectedContentRootSelector, consentSpec, retryAttempted);
        if (!decision.WallDetected) return decision;

        if (decision.CookieName is not null && decision.CookieValue is not null)
        {
            _jar.Set(host, new NetCookie(decision.CookieName, decision.CookieValue, "/", host));
        }

        if (consentSpec is { Strategy: "click" } && !string.IsNullOrWhiteSpace(consentSpec.Name))
        {
            var clickStep = new InteractionStep(PlanOperation.Click, [consentSpec.Name]);
            await _stepExecutor.RunAsync(page, [clickStep], options, cancellationToken).ConfigureAwait(false);
        }

        return decision;
    }

    private static PwCookie ToPlaywrightCookie(NetCookie cookie, string host) => new()
    {
        Name = cookie.Name,
        Value = cookie.Value,
        Domain = string.IsNullOrEmpty(cookie.Domain) ? host : cookie.Domain,
        Path = string.IsNullOrEmpty(cookie.Path) ? "/" : cookie.Path,
        HttpOnly = cookie.HttpOnly,
        Secure = cookie.Secure,
        Expires = cookie.Expires == default ? null : (float)((DateTimeOffset)cookie.Expires.ToUniversalTime()).ToUnixTimeSeconds(),
    };

    private static NetCookie ToNetCookie(BrowserContextCookiesResult cookie)
    {
        var netCookie = new NetCookie(cookie.Name, cookie.Value, cookie.Path, cookie.Domain)
        {
            HttpOnly = cookie.HttpOnly,
            Secure = cookie.Secure,
        };

        if (cookie.Expires > 0)
        {
            netCookie.Expires = DateTimeOffset.FromUnixTimeSeconds((long)cookie.Expires).UtcDateTime;
        }

        return netCookie;
    }
}
