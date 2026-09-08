using Sanare.Core.Fixtures.Hashing;
using Xunit;

namespace Sanare.Core.Tests.Fixtures;

/// <summary>HTML/JSON volatility handling that backs `normalisedHash` (docs/features/fixture-corpus.md
/// AC-FIX-001, AC-FIX-002, AC-FIX-015).</summary>
public sealed class NormalizerTests
{
    private readonly HtmlNormalizer _html = new();
    private readonly JsonNormalizer _json = new();
    private readonly ContentHasher _hasher = new();

    [Fact]
    public void HtmlNormalizer_strips_comments()
    {
        var normalized = _html.Normalize("<div><!-- build id 12345 -->hello</div>");

        Assert.DoesNotContain("build id", normalized);
        Assert.Contains("hello", normalized);
    }

    [Fact]
    public void HtmlNormalizer_strips_a_volatile_nonce_attribute_value()
    {
        var normalized = _html.Normalize("<script nonce=\"abc123\">console.log(1);</script>");

        Assert.DoesNotContain("abc123", normalized);
    }

    [Fact]
    public void HtmlNormalizer_strips_a_volatile_data_timestamp_attribute_value()
    {
        var normalized = _html.Normalize("<main data-timestamp=\"2026-09-06T10:12:00Z\">content</main>");

        Assert.DoesNotContain("2026-09-06T10:12:00Z", normalized);
        Assert.Contains("content", normalized);
    }

    [Fact]
    public void HtmlNormalizer_strips_non_JsonLd_script_bodies()
    {
        var normalized = _html.Normalize("<script>trackPageView('secret-session-42');</script>");

        Assert.DoesNotContain("secret-session-42", normalized);
    }

    [Fact]
    public void HtmlNormalizer_retains_JsonLd_script_bodies()
    {
        const string html = "<script type=\"application/ld+json\">{\"@type\":\"Product\",\"name\":\"Yoga Tab\"}</script>";

        var normalized = _html.Normalize(html);

        Assert.Contains("Yoga Tab", normalized);
    }

    [Fact]
    public void HtmlNormalizer_collapses_whitespace_between_tags()
    {
        var normalized = _html.Normalize("<ul>\n  <li>a</li>\n  <li>b</li>\n</ul>");

        Assert.DoesNotContain("\n", normalized);
    }

    [Fact]
    public void HtmlNormalizer_produces_the_same_output_for_two_captures_differing_only_by_volatile_attributes()
    {
        var first = _html.Normalize("<main data-timestamp=\"2026-09-06T10:00:00Z\"><p>Same content</p></main>");
        var second = _html.Normalize("<main data-timestamp=\"2026-09-06T11:30:00Z\"><p>Same content</p></main>");

        Assert.Equal(first, second);
    }

    [Fact]
    public void JsonNormalizer_sorts_object_keys()
    {
        var normalized = _json.Normalize("{\"b\":1,\"a\":2}");

        Assert.Equal("{\"a\":2,\"b\":1}", normalized);
    }

    [Fact]
    public void JsonNormalizer_drops_the_default_volatile_keys()
    {
        var normalized = _json.Normalize("{\"requestId\":\"req-1\",\"timestamp\":\"now\",\"sessionId\":\"s1\",\"nonce\":\"n1\",\"data\":1}");

        Assert.Equal("{\"data\":1}", normalized);
    }

    [Fact]
    public void JsonNormalizer_produces_the_same_output_for_payloads_differing_only_by_requestId()
    {
        // AC-FIX-015: two captures whose JSON differs only in a volatile `requestId` field dedupe to
        // the same normalisedHash.
        var first = _json.Normalize("{\"requestId\":\"req-0000000001\",\"products\":[{\"sku\":\"1\"}]}");
        var second = _json.Normalize("{\"requestId\":\"req-0000000002\",\"products\":[{\"sku\":\"1\"}]}");

        Assert.Equal(first, second);
    }

    [Fact]
    public void ContentHasher_NormalisedHash_matches_for_html_differing_only_by_volatile_attributes()
    {
        var first = _hasher.NormalisedHash("<main data-timestamp=\"2026-09-06T10:00:00Z\">x</main>"u8, "text/html");
        var second = _hasher.NormalisedHash("<main data-timestamp=\"2026-09-06T11:30:00Z\">x</main>"u8, "text/html");

        Assert.Equal(first, second);
    }

    [Fact]
    public void ContentHasher_ContentHash_differs_for_html_differing_only_by_volatile_attributes()
    {
        // Unlike normalisedHash, the raw contentHash is a byte-for-byte hash and must differ.
        var first = _hasher.ContentHash("<main data-timestamp=\"2026-09-06T10:00:00Z\">x</main>"u8);
        var second = _hasher.ContentHash("<main data-timestamp=\"2026-09-06T11:30:00Z\">x</main>"u8);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void ContentHasher_hashes_are_prefixed_with_sha256()
    {
        var hash = _hasher.ContentHash("hello"u8);

        Assert.StartsWith("sha256:", hash, StringComparison.Ordinal);
    }
}
