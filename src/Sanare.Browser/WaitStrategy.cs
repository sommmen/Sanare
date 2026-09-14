using Microsoft.Playwright;

namespace Sanare.Browser;

/// <summary>
/// A closed union of the wait strategies <c>AcquisitionSpec.WaitFor</c> may name
/// (docs/features/browser-tier.md, T7 "wait strategies"). There is deliberately no case that
/// executes arbitrary page script.
/// </summary>
public abstract record WaitStrategy(TimeSpan Timeout)
{
    public sealed record Load() : WaitStrategy(TimeSpan.FromSeconds(30));

    public sealed record DomContentLoaded() : WaitStrategy(TimeSpan.FromSeconds(30));

    public sealed record NetworkIdle() : WaitStrategy(TimeSpan.FromSeconds(30));

    public sealed record Selector(string CssSelector) : WaitStrategy(TimeSpan.FromSeconds(15));

    public sealed record Function(string PredicateName) : WaitStrategy(TimeSpan.FromSeconds(15));
}

/// <summary>
/// Parses <c>AcquisitionSpec.WaitFor</c> into a closed <see cref="WaitStrategy"/>, and applies it to a
/// live page. Only an allow-listed table of named predicates is ever evaluated for
/// <see cref="WaitStrategy.Function"/> — never model-authored JavaScript.
/// </summary>
public static class WaitStrategyParser
{
    private const string SelectorPrefix = "selector:";
    private const string FunctionPrefix = "function:";

    /// <summary>Allow-listed predicate expressions, keyed by the plan-facing name (e.g. <c>"itemCountAtLeast(3)"</c>).</summary>
    public static IReadOnlyDictionary<string, string> AllowedFunctionPredicates { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["documentReady"] = "document.readyState === 'complete'",
    };

    /// <summary>
    /// Parses <paramref name="waitFor"/>. Returns <see cref="WaitStrategy.Load"/> when
    /// <see langword="null"/>/empty, and throws <see cref="FormatException"/> when the value cannot
    /// be mapped to any closed-union case (used by plan validation to reject the plan up front).
    /// </summary>
    public static WaitStrategy Parse(string? waitFor)
    {
        if (string.IsNullOrWhiteSpace(waitFor)) return new WaitStrategy.Load();

        var value = waitFor.Trim();
        if (string.Equals(value, "load", StringComparison.OrdinalIgnoreCase)) return new WaitStrategy.Load();
        if (string.Equals(value, "domcontentloaded", StringComparison.OrdinalIgnoreCase)) return new WaitStrategy.DomContentLoaded();
        if (string.Equals(value, "networkidle", StringComparison.OrdinalIgnoreCase)) return new WaitStrategy.NetworkIdle();

        if (value.StartsWith(SelectorPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var selector = value[SelectorPrefix.Length..].Trim();
            if (selector.Length == 0) throw new FormatException($"Wait strategy '{waitFor}' is missing a selector.");
            return new WaitStrategy.Selector(selector);
        }

        if (value.StartsWith(FunctionPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var name = value[FunctionPrefix.Length..].Trim();
            if (!AllowedFunctionPredicates.ContainsKey(name))
            {
                throw new FormatException($"Wait strategy function '{name}' is not an allow-listed predicate.");
            }

            return new WaitStrategy.Function(name);
        }

        throw new FormatException($"Wait strategy '{waitFor}' could not be parsed.");
    }

    /// <summary>
    /// Attempts to <see cref="Parse"/> without throwing, for use by plan validation to reject an
    /// unparsable <c>waitFor</c> value before the browser tier ever runs.
    /// </summary>
    public static bool TryParse(string? waitFor, out WaitStrategy? strategy)
    {
        try
        {
            strategy = Parse(waitFor);
            return true;
        }
        catch (FormatException)
        {
            strategy = null;
            return false;
        }
    }

    /// <summary>
    /// Applies the strategy to <paramref name="page"/>, letting a timeout propagate as a
    /// <see cref="TimeoutException"/> so the caller can capture the partial DOM before mapping it to
    /// <c>SNR-BRW-003</c>.
    /// </summary>
    public static async Task ApplyAsync(this WaitStrategy strategy, IPage page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(page);
        cancellationToken.ThrowIfCancellationRequested();

        var timeoutMs = (float)strategy.Timeout.TotalMilliseconds;
        switch (strategy)
        {
            case WaitStrategy.Load:
                await page.WaitForLoadStateAsync(LoadState.Load, new PageWaitForLoadStateOptions { Timeout = timeoutMs }).ConfigureAwait(false);
                break;
            case WaitStrategy.DomContentLoaded:
                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new PageWaitForLoadStateOptions { Timeout = timeoutMs }).ConfigureAwait(false);
                break;
            case WaitStrategy.NetworkIdle:
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = timeoutMs }).ConfigureAwait(false);
                break;
            case WaitStrategy.Selector selector:
                await page.WaitForSelectorAsync(selector.CssSelector, new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = timeoutMs }).ConfigureAwait(false);
                break;
            case WaitStrategy.Function function:
                var expression = AllowedFunctionPredicates[function.PredicateName];
                await page.WaitForFunctionAsync(expression, arg: null, new PageWaitForFunctionOptions { Timeout = timeoutMs }).ConfigureAwait(false);
                break;
            default:
                throw new NotSupportedException($"Unrecognized wait strategy '{strategy.GetType().Name}'.");
        }
    }
}
