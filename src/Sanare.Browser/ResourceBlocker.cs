using Microsoft.Playwright;
using Sanare.Core.Acquisition;

namespace Sanare.Browser;

/// <summary>
/// Aborts image/media/font requests and configured block-list hosts on a context
/// (docs/features/browser-tier.md, T9 "resource blocking"). Skipped entirely when the wait strategy
/// depends on network idle and the resolved source is flagged as requiring images, so lazy-loading
/// listers still render.
/// </summary>
public static class ResourceBlocker
{
    private static readonly HashSet<string> BlockedResourceTypes = new(StringComparer.OrdinalIgnoreCase) { "image", "media", "font" };

    /// <summary>Whether blocking should be applied for the given wait strategy / source policy combination.</summary>
    public static bool ShouldBlock(WaitStrategy waitStrategy, bool requiresImages)
    {
        ArgumentNullException.ThrowIfNull(waitStrategy);
        return !(waitStrategy is WaitStrategy.NetworkIdle && requiresImages);
    }

    /// <summary>Installs the blocking route on <paramref name="context"/> for the lifetime of the context.</summary>
    public static Task InstallAsync(IBrowserContext context, BrowserOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        var blockedHosts = options.EffectiveBlockedHosts;
        return context.RouteAsync("**/*", async route =>
        {
            var request = route.Request;
            if (BlockedResourceTypes.Contains(request.ResourceType) || MatchesBlockedHost(request.Url, blockedHosts))
            {
                await route.AbortAsync().ConfigureAwait(false);
            }
            else
            {
                await route.ContinueAsync().ConfigureAwait(false);
            }
        });
    }

    private static bool MatchesBlockedHost(string url, IReadOnlyList<string> blockedHosts)
    {
        if (blockedHosts.Count == 0) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)) return false;

        foreach (var host in blockedHosts)
        {
            if (parsed.Host.Equals(host, StringComparison.OrdinalIgnoreCase) || parsed.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
