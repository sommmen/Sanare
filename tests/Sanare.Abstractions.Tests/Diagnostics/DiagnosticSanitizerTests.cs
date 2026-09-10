using Sanare.Abstractions.Diagnostics;

namespace Sanare.Abstractions.Tests.Diagnostics;

public sealed class DiagnosticSanitizerTests
{
    private static readonly ScrapeDiagnostic DiagnosticWithDetail = new(
        "SNR-API-001", DiagnosticSeverity.Error, "Request URL must be an absolute http(s) URI.",
        Detail: "raw upstream response body");

    [Fact]
    public void Sanitize_clears_detail_when_detail_reporting_is_disabled()
    {
        var sanitized = DiagnosticSanitizer.Sanitize(DiagnosticWithDetail, includeDetail: false);

        Assert.Null(sanitized.Detail);
        Assert.Equal(DiagnosticWithDetail.Message, sanitized.Message);
        Assert.Equal(DiagnosticWithDetail.Code, sanitized.Code);
        Assert.Equal(DiagnosticWithDetail.Severity, sanitized.Severity);
    }

    [Fact]
    public void Sanitize_preserves_detail_when_detail_reporting_is_enabled()
    {
        var sanitized = DiagnosticSanitizer.Sanitize(DiagnosticWithDetail, includeDetail: true);

        Assert.Same(DiagnosticWithDetail, sanitized);
        Assert.Equal(DiagnosticWithDetail.Detail, sanitized.Detail);
    }

    [Fact]
    public void Sanitize_throws_for_a_null_diagnostic()
    {
        Assert.Throws<ArgumentNullException>(() => DiagnosticSanitizer.Sanitize(null!, includeDetail: false));
    }

    [Fact]
    public void SanitizeUrl_drops_userinfo_query_and_fragment()
    {
        var uri = new Uri("https://user:pass@www.example.com/tablets/yoga-tab?token=secret#section");

        var sanitized = DiagnosticSanitizer.SanitizeUrl(uri);

        Assert.Equal("https://www.example.com/tablets/yoga-tab", sanitized);
        Assert.DoesNotContain("user", sanitized);
        Assert.DoesNotContain("pass", sanitized);
        Assert.DoesNotContain("token", sanitized);
    }

    [Fact]
    public void SanitizeUrl_throws_for_a_null_uri()
    {
        Assert.Throws<ArgumentNullException>(() => DiagnosticSanitizer.SanitizeUrl(null!));
    }
}
