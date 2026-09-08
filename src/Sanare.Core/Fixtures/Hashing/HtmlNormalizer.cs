using System.Text;
using System.Text.RegularExpressions;

namespace Sanare.Core.Fixtures.Hashing;

public sealed partial class HtmlNormalizer
{
    public string Normalize(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        var value = Comments().Replace(html, string.Empty);
        value = Scripts().Replace(value, match => match.Groups[1].Value.Contains("application/ld+json", StringComparison.OrdinalIgnoreCase) ? match.Value : string.Empty);
        value = VolatileAttributes().Replace(value, "$1=\"\"");
        return BetweenTagsWhitespace().Replace(value, "><").Trim();
    }

    [GeneratedRegex("<!--.*?-->", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Comments();
    [GeneratedRegex("(<script\\b[^>]*>).*?</script\\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Scripts();
    [GeneratedRegex("\\b(nonce|csrf|data-timestamp)\\s*=\\s*(?:\"[^\"]*\"|'[^']*'|[^\\s>]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VolatileAttributes();
    [GeneratedRegex(@">\s+<", RegexOptions.CultureInvariant)]
    private static partial Regex BetweenTagsWhitespace();
}
