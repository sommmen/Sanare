using System.Globalization;

namespace Sanare.Http.Robots;

/// <summary>
/// Parses a <c>robots.txt</c> document into the rule set applying to one user-agent
/// (docs/features/acquisition-pipeline.md, "Key Behaviors" &gt; "robots.txt").
/// </summary>
/// <remarks>
/// Malformed input is skipped, never fatal. A robots file is a third party's artefact that Sanare does
/// not control, so a single unparseable line must not deny access to a host the operator is entitled to
/// fetch. Unrecognised directives, missing colons, and unparseable delays are all dropped silently.
/// </remarks>
public static class RobotsTxtParser
{
    /// <summary>
    /// Parses <paramref name="content"/>, selecting the group whose <c>User-agent</c> matches
    /// <paramref name="userAgentToken"/> and falling back to the <c>*</c> group when none does.
    /// </summary>
    /// <param name="content">The raw <c>robots.txt</c> body.</param>
    /// <param name="userAgentToken">The identity token to match, e.g. <c>Sanare</c>.</param>
    public static RobotsRuleSet Parse(string content, string userAgentToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(userAgentToken);

        var groups = new List<Group>();
        var sitemaps = new List<string>();
        Group? current = null;
        var expectingAgents = false;

        foreach (var rawLine in content.Split('\n'))
        {
            var line = Strip(rawLine);
            if (line.Length == 0)
            {
                continue;
            }

            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            var directive = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();

            if (directive.Equals("user-agent", StringComparison.OrdinalIgnoreCase))
            {
                if (value.Length == 0)
                {
                    continue;
                }

                // Consecutive User-agent lines share one group of rules.
                if (current is null || !expectingAgents)
                {
                    current = new Group();
                    groups.Add(current);
                    expectingAgents = true;
                }

                current.Agents.Add(value);
                continue;
            }

            if (directive.Equals("sitemap", StringComparison.OrdinalIgnoreCase))
            {
                if (value.Length > 0) { sitemaps.Add(value); }
                continue;
            }

            if (current is null)
            {
                continue;
            }

            expectingAgents = false;
            if (directive.Equals("disallow", StringComparison.OrdinalIgnoreCase))
            {
                // "Disallow:" with an empty value means "nothing is disallowed" — it is not a rule.
                if (value.Length > 0) { current.Rules.Add(new RobotsRule(RobotsRuleKind.Disallow, Normalize(value))); }
            }
            else if (directive.Equals("allow", StringComparison.OrdinalIgnoreCase))
            {
                if (value.Length > 0) { current.Rules.Add(new RobotsRule(RobotsRuleKind.Allow, Normalize(value))); }
            }
            else if (directive.Equals("crawl-delay", StringComparison.OrdinalIgnoreCase))
            {
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds is > 0 and < 86_400)
                {
                    current.CrawlDelay = TimeSpan.FromSeconds(seconds);
                }
            }
        }

        var selected = Select(groups, userAgentToken);
        return selected is null
            ? RobotsRuleSet.Unrestricted with { Sitemaps = sitemaps }
            : new RobotsRuleSet(selected.Value.Group.Rules, selected.Value.Group.CrawlDelay, sitemaps, selected.Value.Token);
    }

    private static (Group Group, string Token)? Select(List<Group> groups, string userAgentToken)
    {
        foreach (var group in groups)
        {
            foreach (var agent in group.Agents)
            {
                // Substring rather than equality: robots convention matches a product token inside a
                // longer agent string, and operators commonly write "Sanare-bot" for "Sanare".
                if (agent.Contains(userAgentToken, StringComparison.OrdinalIgnoreCase)
                    || userAgentToken.Contains(agent, StringComparison.OrdinalIgnoreCase))
                {
                    return (group, agent);
                }
            }
        }

        foreach (var group in groups)
        {
            if (group.Agents.Contains("*", StringComparer.Ordinal))
            {
                return (group, "*");
            }
        }

        return null;
    }

    private static string Strip(string line)
    {
        var comment = line.IndexOf('#', StringComparison.Ordinal);
        return (comment >= 0 ? line[..comment] : line).Trim();
    }

    private static string Normalize(string value) => value.StartsWith('/') ? value : "/" + value;

    private sealed class Group
    {
        public List<string> Agents { get; } = [];

        public List<RobotsRule> Rules { get; } = [];

        public TimeSpan? CrawlDelay { get; set; }
    }
}
