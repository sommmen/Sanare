namespace Sanare.Http.Robots;

/// <summary>Whether a rule grants or withholds access to a path prefix.</summary>
public enum RobotsRuleKind
{
    /// <summary>The rule withholds access — a <c>Disallow</c> line.</summary>
    Disallow,

    /// <summary>The rule grants access — an <c>Allow</c> line.</summary>
    Allow,
}

/// <summary>One <c>Allow</c> or <c>Disallow</c> line from a <c>robots.txt</c> group.</summary>
/// <param name="Kind">Whether the rule grants or withholds.</param>
/// <param name="Pattern">The path pattern, which may contain <c>*</c> wildcards and a trailing <c>$</c> anchor.</param>
public sealed record RobotsRule(RobotsRuleKind Kind, string Pattern)
{
    /// <summary>
    /// The rule's specificity for longest-match-wins arbitration: the pattern length excluding the
    /// trailing anchor.
    /// </summary>
    public int Specificity { get; } = Pattern.EndsWith('$') ? Pattern.Length - 1 : Pattern.Length;

    /// <summary>Returns whether <paramref name="path"/> is covered by this rule's pattern.</summary>
    /// <param name="path">The request path and query, beginning with <c>/</c>.</param>
    public bool Matches(string path) => RobotsPathMatcher.Matches(Pattern, path);
}

/// <summary>
/// The parsed <c>robots.txt</c> directives applying to one user-agent, plus the host-level directives
/// that are not agent-scoped
/// (docs/features/acquisition-pipeline.md, "Key Behaviors" &gt; "robots.txt").
/// </summary>
/// <param name="Rules">The <c>Allow</c>/<c>Disallow</c> rules in file order.</param>
/// <param name="CrawlDelay">The advertised <c>Crawl-delay</c>, when present and parseable.</param>
/// <param name="Sitemaps">Every <c>Sitemap</c> URL declared in the file, in file order.</param>
/// <param name="MatchedUserAgent">The user-agent token whose group was selected, or <c>*</c> for the fallback.</param>
public sealed record RobotsRuleSet(
    IReadOnlyList<RobotsRule> Rules,
    TimeSpan? CrawlDelay,
    IReadOnlyList<string> Sitemaps,
    string MatchedUserAgent)
{
    /// <summary>A rule set granting unrestricted access, used when no robots file applies.</summary>
    public static RobotsRuleSet Unrestricted { get; } = new([], null, [], "*");

    /// <summary>
    /// Returns whether <paramref name="path"/> may be fetched. Longest match wins; an <c>Allow</c> beats a
    /// <c>Disallow</c> of equal specificity, matching the convention every major crawler follows. A
    /// zero-length <c>Disallow:</c> value grants access to everything and is ignored here because the
    /// parser already drops it.
    /// </summary>
    /// <param name="path">The request path and query, beginning with <c>/</c>.</param>
    public bool IsAllowed(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        RobotsRule? winner = null;
        foreach (var rule in Rules)
        {
            if (!rule.Matches(path))
            {
                continue;
            }

            if (winner is null
                || rule.Specificity > winner.Specificity
                || (rule.Specificity == winner.Specificity && rule.Kind == RobotsRuleKind.Allow))
            {
                winner = rule;
            }
        }

        return winner is null || winner.Kind == RobotsRuleKind.Allow;
    }
}

/// <summary>Matches <c>robots.txt</c> path patterns, which support <c>*</c> and a trailing <c>$</c>.</summary>
internal static class RobotsPathMatcher
{
    /// <summary>Returns whether <paramref name="path"/> satisfies <paramref name="pattern"/>.</summary>
    /// <param name="pattern">The rule pattern.</param>
    /// <param name="path">The request path and query.</param>
    public static bool Matches(string pattern, string path)
    {
        if (pattern.Length == 0)
        {
            return false;
        }

        var anchored = pattern.EndsWith('$');
        var effective = anchored ? pattern[..^1] : pattern;
        return Match(effective.AsSpan(), path.AsSpan(), anchored);
    }

    private static bool Match(ReadOnlySpan<char> pattern, ReadOnlySpan<char> path, bool anchored)
    {
        var star = pattern.IndexOf('*');
        if (star < 0)
        {
            return anchored
                ? path.Equals(pattern, StringComparison.Ordinal)
                : path.StartsWith(pattern, StringComparison.Ordinal);
        }

        var head = pattern[..star];
        if (!path.StartsWith(head, StringComparison.Ordinal))
        {
            return false;
        }

        var tail = pattern[(star + 1)..];
        if (tail.IsEmpty)
        {
            return !anchored || true;
        }

        // Try every split point the wildcard could consume. Patterns are short, so the quadratic worst
        // case is irrelevant next to the clarity of not hand-rolling a backtracking engine.
        var rest = path[head.Length..];
        for (var skip = 0; skip <= rest.Length; skip++)
        {
            if (Match(tail, rest[skip..], anchored))
            {
                return true;
            }
        }

        return false;
    }
}
