using Sanare.Http.Robots;

namespace Sanare.Http.Tests.Acquisition;

public sealed class RobotsTxtParserTests
{
    [Fact]
    public void A_disallow_blocks_only_matching_paths()
    {
        var rules = RobotsTxtParser.Parse("User-agent: *\nDisallow: /tablets\n", "Sanare");

        Assert.False(rules.IsAllowed("/tablets"));
        Assert.False(rules.IsAllowed("/tablets/lenovo"));
        Assert.True(rules.IsAllowed("/laptops"));
    }

    [Fact]
    public void The_longest_matching_rule_wins()
    {
        var rules = RobotsTxtParser.Parse("User-agent: *\nDisallow: /products\nAllow: /products/public\n", "Sanare");

        Assert.False(rules.IsAllowed("/products/private"));
        Assert.True(rules.IsAllowed("/products/public/item"));
    }

    [Fact]
    public void An_empty_disallow_grants_full_access()
    {
        var rules = RobotsTxtParser.Parse("User-agent: *\nDisallow:\n", "Sanare");
        Assert.True(rules.IsAllowed("/anything"));
    }

    [Fact]
    public void A_named_group_is_preferred_over_the_wildcard_group()
    {
        var rules = RobotsTxtParser.Parse(
            "User-agent: *\nDisallow: /\n\nUser-agent: Sanare\nDisallow: /admin\n",
            "Sanare");

        Assert.True(rules.IsAllowed("/products"));
        Assert.False(rules.IsAllowed("/admin"));
    }

    [Fact]
    public void Wildcards_and_anchors_are_honoured()
    {
        var rules = RobotsTxtParser.Parse("User-agent: *\nDisallow: /*.pdf$\n", "Sanare");

        Assert.False(rules.IsAllowed("/docs/manual.pdf"));
        Assert.True(rules.IsAllowed("/docs/manual.pdf.html"));
    }

    [Fact]
    public void Crawl_delay_is_parsed()
    {
        var rules = RobotsTxtParser.Parse("User-agent: *\nCrawl-delay: 10\n", "Sanare");
        Assert.Equal(TimeSpan.FromSeconds(10), rules.CrawlDelay);
    }

    [Fact]
    public void Sitemap_references_are_collected_regardless_of_group()
    {
        var rules = RobotsTxtParser.Parse(
            "Sitemap: https://example.test/llms.txt\nUser-agent: *\nDisallow: /admin\n",
            "Sanare");

        Assert.Contains("https://example.test/llms.txt", rules.Sitemaps);
    }

    [Fact]
    public void Comments_and_malformed_lines_are_ignored()
    {
        var rules = RobotsTxtParser.Parse(
            "# a comment\nthis line is nonsense\nUser-agent: *\nDisallow: /admin # trailing\n",
            "Sanare");

        Assert.False(rules.IsAllowed("/admin"));
        Assert.True(rules.IsAllowed("/products"));
    }

    [Fact]
    public void An_empty_document_grants_full_access()
    {
        Assert.True(RobotsTxtParser.Parse(string.Empty, "Sanare").IsAllowed("/anything"));
    }
}
