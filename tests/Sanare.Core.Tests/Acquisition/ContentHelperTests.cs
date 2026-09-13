using System.Text;
using Sanare.Core.Acquisition;
using Sanare.Core.Acquisition.Content;

namespace Sanare.Core.Tests.Acquisition;

/// <summary>
/// Covers the three response-body helpers named in the acquisition-pipeline Test Module's "Unit"
/// scope: <see cref="CharsetDetector"/> precedence, <see cref="ContentTypeGate"/>, and
/// <see cref="BoundedStreamReader"/> at the exact ceiling.
/// </summary>
public sealed class ContentHelperTests
{
    private static readonly IReadOnlySet<string> HtmlOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "text/html" };

    [Fact]
    public void A_declared_header_charset_outranks_every_other_signal()
    {
        // The body carries a UTF-8 BOM *and* a conflicting meta tag; the header still wins.
        var body = new List<byte>();
        body.AddRange([0xEF, 0xBB, 0xBF]);
        body.AddRange(Encoding.ASCII.GetBytes("<meta charset=\"utf-16\">"));

        var resolution = CharsetDetector.Resolve("iso-8859-1", body.ToArray());

        Assert.Equal(CharsetSource.Header, resolution.Source);
        Assert.Equal("iso-8859-1", resolution.Encoding.WebName);
        Assert.Null(resolution.Warning);
    }

    [Fact]
    public void A_byte_order_mark_outranks_a_meta_declaration()
    {
        var body = new List<byte>();
        body.AddRange([0xEF, 0xBB, 0xBF]);
        body.AddRange(Encoding.ASCII.GetBytes("<meta charset=\"iso-8859-1\">"));

        var resolution = CharsetDetector.Resolve(headerCharset: null, body.ToArray());

        Assert.Equal(CharsetSource.ByteOrderMark, resolution.Source);
        Assert.Equal(Encoding.UTF8.WebName, resolution.Encoding.WebName);
        Assert.Null(resolution.Warning);
    }

    [Theory]
    [InlineData("<meta charset=\"iso-8859-1\">")]
    [InlineData("<meta http-equiv=\"Content-Type\" content=\"text/html; charset=iso-8859-1\">")]
    public void A_meta_declaration_is_honoured_in_both_wire_forms(string markup)
    {
        var resolution = CharsetDetector.Resolve(headerCharset: null, Encoding.ASCII.GetBytes(markup));

        Assert.Equal(CharsetSource.MetaTag, resolution.Source);
        Assert.Equal("iso-8859-1", resolution.Encoding.WebName);
    }

    [Fact]
    public void A_meta_declaration_beyond_the_first_8_KiB_is_not_consulted()
    {
        var padding = new string(' ', 8192);
        var body = Encoding.ASCII.GetBytes(padding + "<meta charset=\"iso-8859-1\">");

        var resolution = CharsetDetector.Resolve(headerCharset: null, body);

        Assert.Equal(CharsetSource.Fallback, resolution.Source);
    }

    [Fact]
    public void An_undeclared_encoding_falls_back_to_utf8_and_says_so()
    {
        // The fallback is a guess, so AC-ACQ-017 requires it to surface rather than happen silently.
        var resolution = CharsetDetector.Resolve(headerCharset: null, Encoding.ASCII.GetBytes("<p>no declaration</p>"));

        Assert.Equal(CharsetSource.Fallback, resolution.Source);
        Assert.Equal(Encoding.UTF8.WebName, resolution.Encoding.WebName);
        Assert.NotNull(resolution.Warning);
        Assert.Equal("SNR-ACQ-014", resolution.Warning.Code);
    }

    [Fact]
    public void An_unrecognised_header_charset_falls_through_rather_than_throwing()
    {
        var resolution = CharsetDetector.Resolve("not-a-real-charset", Encoding.ASCII.GetBytes("<p>hi</p>"));

        Assert.Equal(CharsetSource.Fallback, resolution.Source);
    }

    [Fact]
    public void The_content_type_gate_admits_an_expected_type()
    {
        ContentTypeGate.Validate("text/html", HtmlOnly);

        Assert.True(ContentTypeGate.IsAllowed("text/html", HtmlOnly));
    }

    [Fact]
    public void The_content_type_gate_rejects_an_unexpected_type_with_SNR_ACQ_006()
    {
        var error = Assert.Throws<AcquisitionException>(() => ContentTypeGate.Validate("application/pdf", HtmlOnly));

        Assert.Equal("SNR-ACQ-006", error.Code);
        Assert.False(ContentTypeGate.IsAllowed("application/pdf", HtmlOnly));
    }

    [Fact]
    public async Task A_body_at_exactly_the_ceiling_is_read_in_full()
    {
        var payload = new byte[4096];
        Random.Shared.NextBytes(payload);

        var read = await BoundedStreamReader.ReadAsync(new MemoryStream(payload), payload.Length, "SNR-ACQ-007");

        Assert.Equal(payload, read);
    }

    [Fact]
    public async Task A_body_one_byte_over_the_ceiling_throws_the_supplied_code()
    {
        var payload = new byte[4097];

        var error = await Assert.ThrowsAsync<AcquisitionException>(
            async () => await BoundedStreamReader.ReadAsync(new MemoryStream(payload), 4096, "SNR-ACQ-007"));

        Assert.Equal("SNR-ACQ-007", error.Code);
    }

    [Fact]
    public async Task The_overflow_code_is_the_callers_choice_so_discovery_documents_report_SNR_ACQ_010()
    {
        var payload = new byte[64];

        var error = await Assert.ThrowsAsync<AcquisitionException>(
            async () => await BoundedStreamReader.ReadAsync(new MemoryStream(payload), 32, "SNR-ACQ-010"));

        Assert.Equal("SNR-ACQ-010", error.Code);
    }
}
