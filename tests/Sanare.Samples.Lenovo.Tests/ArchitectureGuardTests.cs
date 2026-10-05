using System.Text.RegularExpressions;

namespace Sanare.Samples.Lenovo.Tests;

/// <summary>
/// Proves the product claim that the sample contains no scraping knowledge of its own: every
/// CSS selector, XPath expression, JSON pointer into a Lenovo payload, or regex used to pull data
/// out of the Lenovo pages lives only in the committed plan JSON under
/// <c>samples/Sanare.Samples.Lenovo.State/scripts/plans/</c>, never in this project's C# source.
/// </summary>
public sealed class ArchitectureGuardTests
{
    /// <summary>
    /// The only Lenovo identifiers the sample is allowed to name directly: the two source ids and
    /// the two start URLs declared as constants in <c>Program.cs</c>.
    /// </summary>
    private static readonly string[] AllowedLenovoUrls =
    [
        "https://www.lenovo.com/nl/nl/p/tablets/android-tablets/yoga-tab-series/lenovo-yoga-tab-gen-2/len103y0003",
        "https://www.lenovo.com/nl/nl/tablets/",
    ];

    private static readonly Regex LenovoUrlPattern = new(
        @"https?://(?:[\w-]+\.)*lenovo\.com[^\s""]*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Matches a whole C# string literal so each one can be tested in isolation.</summary>
    private static readonly Regex StringLiteral = new(
        @"""(?:[^""\\]|\\.)*""",
        RegexOptions.Compiled);

    // A small set of common HTML tag names, used only to recognize "tag.class"/"tag#id"
    // compound selectors (e.g. "div.product-name") without also flagging lowercase file
    // basenames such as "tablet-detail.json", which have the same "word.word" shape.
    private const string HtmlTagNames =
        "a|p|div|span|ul|ol|li|dl|dt|dd|table|tr|td|th|thead|tbody|tfoot|form|input|select|option|" +
        "textarea|label|button|img|script|style|link|meta|head|body|html|nav|header|footer|section|" +
        "article|aside|main|figure|figcaption|video|audio|source|iframe|svg|path|canvas|code|pre|" +
        "blockquote|em|strong|small|h1|h2|h3|h4|h5|h6|br|hr";

    // A string literal is CSS-selector-looking when it contains one of CSS's distinctive,
    // selector-only markers: a leading class/id selector, a known-tag-qualified class/id
    // selector (e.g. "div.product-name", "a#main"), an attribute **value** selector (bare
    // presence selectors like "[disabled]" are too easily confused with plain bracketed usage
    // text and are intentionally not matched), a pseudo-class/element call, or a child/sibling
    // combinator surrounded by spaces. Plain dotted namespaces, file paths, and "[--flag]"-style
    // usage text (e.g. "Sanare.Samples.Lenovo.Tests", "tablet-detail.json", "[--offline]") use
    // none of these and so do not match.
    private static readonly Regex CssSelectorLikeLiteral = new(
        $@"(?:^[.#][\w-]+)|(?:^(?:{HtmlTagNames})[.#][\w-]+)|(?:\[[\w-]+[~^$*|]?=[^\]]*\])|(?:::?[a-zA-Z-]+\()|(?:\s[>~+]\s)",
        RegexOptions.Compiled);

    // An XPath-looking literal is, as a whole, an absolute/relative path or predicate expression
    // starting with '/', './', or '../' and using distinctive XPath syntax (an attribute
    // selector, a predicate, or a function call) so that plain RFC 6901 JSON pointers such as
    // "/Specifications/" — which this sample legitimately uses to address its own schema, not
    // Lenovo's payload — are not mistaken for XPath.
    private static readonly Regex XPathLikeLiteral = new(
        @"^(?=.*[@\[(])(?:\.{0,2}/)[\w/@*\[\]().='""\s-]*$",
        RegexOptions.Compiled);

    // A JSONPath-into-a-Lenovo-payload literal starts with '$.' or '$[' — the grammar documented
    // on the JsonPath locator in Sanare.Core.
    private static readonly Regex JsonPathLikeLiteral = new(
        @"^\$[.\[][\w\[\]*.]*$",
        RegexOptions.Compiled);

    // A content-regex-looking literal uses regex metasyntax that has no purpose in ordinary prose or
    // configuration text: a character class, a capture/non-capture group, an escape class such as \w
    // or \d, or an anchored quantifier. Matching on these markers rather than on "contains a dot or
    // star" keeps file names, format strings, and usage text from tripping the guard, while a real
    // extraction pattern — for example "SKU: (\w+)" or "price-([\d,.]+)" — is caught.
    private static readonly Regex RegexLikeLiteral = new(
        @"(?:\[\^?[^\]]*\][*+?{])|(?:\((?:\?[:=!<][^)]*|[^)]*)\)[*+?]?)|(?:\\[wdsWDSbB])|(?:[.\w)\]][*+][?]?(?:$|[^\w]))|(?:^\^)|(?:\$$)",
        RegexOptions.Compiled);

    [Fact]
    public void Sample_source_declares_no_lenovo_urls_beyond_the_two_start_urls()
    {
        foreach (var file in EnumerateSampleSourceFiles())
        {
            var text = File.ReadAllText(file);
            foreach (Match match in LenovoUrlPattern.Matches(text))
            {
                Assert.True(
                    AllowedLenovoUrls.Contains(match.Value),
                    $"{file} references an undeclared lenovo.com URL: {match.Value}");
            }
        }
    }

    [Fact]
    public void Sample_source_contains_no_css_selector_literals()
    {
        AssertNoLiteralMatches(CssSelectorLikeLiteral, "a CSS-selector-looking");
    }

    [Fact]
    public void Sample_source_contains_no_xpath_literals()
    {
        AssertNoLiteralMatches(XPathLikeLiteral, "an XPath-looking");
    }

    [Fact]
    public void Sample_source_contains_no_json_path_literals_into_lenovo_payloads()
    {
        AssertNoLiteralMatches(JsonPathLikeLiteral, "a JSONPath-looking");
    }

    [Fact]
    public void Sample_source_contains_no_content_regex_literals()
    {
        AssertNoLiteralMatches(RegexLikeLiteral, "a content-regex-looking");
    }

    private static void AssertNoLiteralMatches(Regex literalShape, string description)
    {
        foreach (var file in EnumerateSampleSourceFiles())
        {
            var text = File.ReadAllText(file);
            foreach (Match literal in StringLiteral.Matches(text))
            {
                var inner = literal.Value[1..^1];
                Assert.False(
                    literalShape.IsMatch(inner),
                    $"{file} contains {description} string literal: {literal.Value}");
            }
        }
    }

    private static IEnumerable<string> EnumerateSampleSourceFiles()
    {
        var sampleDirectory = Path.Combine(FindRepositoryRoot(), "samples", "Sanare.Samples.Lenovo");
        return Directory.EnumerateFiles(sampleDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Sanare.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
