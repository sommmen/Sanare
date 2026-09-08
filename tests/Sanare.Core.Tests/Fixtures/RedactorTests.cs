using System.Text;
using Sanare.Core.Fixtures.Redaction;
using Xunit;

namespace Sanare.Core.Tests.Fixtures;

/// <summary>Redaction coverage: cookies, emails, IBANs, password fields (docs/features/fixture-corpus.md
/// AC-FIX-003, AC-FIX-004). Redaction has no disable toggle on the public API surface — <see cref="IRedactor"/>
/// exposes a single unconditional <see cref="IRedactor.Redact"/> method.</summary>
public sealed class RedactorTests
{
    private readonly Redactor _redactor = new();

    [Fact]
    public void Redact_drops_Set_Cookie_header_and_records_a_cookie_rule()
    {
        var headers = new Dictionary<string, string> { ["Set-Cookie"] = "sessionid=abc123; Path=/", ["Content-Type"] = "text/html" };

        var result = _redactor.Redact("<html></html>"u8, headers);

        Assert.DoesNotContain("Set-Cookie", result.Headers.Keys);
        Assert.Contains("Content-Type", result.Headers.Keys);
        Assert.Contains("cookie", result.Rules);
    }

    [Fact]
    public void Redact_drops_Authorization_and_Proxy_Authorization_headers()
    {
        var headers = new Dictionary<string, string> { ["Authorization"] = "Bearer secret-token", ["Proxy-Authorization"] = "Basic xyz" };

        var result = _redactor.Redact("<html></html>"u8, headers);

        Assert.Empty(result.Headers);
        Assert.Contains("cookie", result.Rules);
    }

    [Fact]
    public void Redact_replaces_email_addresses_and_records_an_email_rule()
    {
        var content = Encoding.UTF8.GetBytes("Contact support@example.com for help.");

        var result = _redactor.Redact(content);
        var text = Encoding.UTF8.GetString(result.Content);

        Assert.DoesNotContain("support@example.com", text);
        Assert.Contains("redacted@example.invalid", text);
        Assert.Contains("email", result.Rules);
    }

    [Fact]
    public void Redact_replaces_an_IBAN_and_records_an_iban_rule()
    {
        var content = Encoding.UTF8.GetBytes("IBAN: NL91ABNA0417164300");

        var result = _redactor.Redact(content);
        var text = Encoding.UTF8.GetString(result.Content);

        Assert.DoesNotContain("NL91ABNA0417164300", text);
        Assert.Contains("[REDACTED]", text);
        Assert.Contains("iban", result.Rules);
    }

    [Fact]
    public void Redact_replaces_a_hidden_password_input_value_and_records_a_credential_rule()
    {
        var content = Encoding.UTF8.GetBytes("<input type=\"hidden\" name=\"password\" value=\"hunter2secretvalue\" />");

        var result = _redactor.Redact(content);
        var text = Encoding.UTF8.GetString(result.Content);

        Assert.DoesNotContain("hunter2secretvalue", text);
        Assert.Contains("[REDACTED]", text);
        Assert.Contains("credential", result.Rules);
    }

    [Fact]
    public void Redact_replaces_a_JSON_style_password_field_and_records_a_credential_rule()
    {
        var content = Encoding.UTF8.GetBytes("{\"password\": \"hunter2secretvalue\", \"user\": \"jdoe\"}");

        var result = _redactor.Redact(content);
        var text = Encoding.UTF8.GetString(result.Content);

        Assert.DoesNotContain("hunter2secretvalue", text);
        Assert.Contains("jdoe", text);
        Assert.Contains("credential", result.Rules);
    }

    [Fact]
    public void Redact_leaves_content_with_no_sensitive_data_unchanged_and_records_no_rules()
    {
        var content = Encoding.UTF8.GetBytes("<html><body>Just a normal product page.</body></html>");

        var result = _redactor.Redact(content);

        Assert.Equal(content, result.Content);
        Assert.Empty(result.Rules);
    }

    [Fact]
    public void Redact_processes_the_synthetic_pii_sample_fixture_and_removes_all_targeted_pii()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Data", "pii-sample.html");
        var content = File.ReadAllBytes(path);

        var result = _redactor.Redact(content);
        var text = Encoding.UTF8.GetString(result.Content);

        Assert.DoesNotContain("support@example.com", text);
        Assert.DoesNotContain("NL91ABNA0417164300", text);
        Assert.DoesNotContain("hunter2secretvalue", text);
        Assert.Contains("email", result.Rules);
        Assert.Contains("iban", result.Rules);
        Assert.Contains("credential", result.Rules);
    }
}
