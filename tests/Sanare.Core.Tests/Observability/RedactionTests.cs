using Microsoft.Extensions.Logging;
using Sanare.Core.Observability.Redaction;

namespace Sanare.Core.Tests.Observability;

public sealed class RedactionTests
{
    [Theory]
    [InlineData("Authorization")]
    [InlineData("Cookie")]
    [InlineData("Set-Cookie")]
    public void RedactValue_Redacts_sensitive_headers_regardless_of_case(string headerName)
    {
        var policy = new RedactionPolicy();

        var result = policy.RedactValue(headerName.ToUpperInvariant(), "Bearer super-secret-token");

        Assert.Equal(RedactionPolicy.Redacted, result);
    }

    [Fact]
    public void RedactValue_Redacts_an_authorization_header_value_so_it_never_reaches_a_log_line()
    {
        var policy = new RedactionPolicy();
        var state = new[] { new KeyValuePair<string, object?>("Authorization", "Bearer abc123") };

        var result = state.Select(pair => new KeyValuePair<string, object?>(pair.Key, policy.RedactValue(pair.Key, pair.Value))).ToArray();

        Assert.DoesNotContain(result, pair => Equals(pair.Value, "Bearer abc123"));
        Assert.Equal(RedactionPolicy.Redacted, result[0].Value);
    }

    [Fact]
    public void Redact_Replaces_an_email_address()
    {
        var policy = new RedactionPolicy();

        var result = policy.Redact("Contact us at person@example.com for details.");

        Assert.DoesNotContain("person@example.com", result, StringComparison.Ordinal);
        Assert.Contains(RedactionPolicy.Redacted, result, StringComparison.Ordinal);
    }

    [Fact]
    public void Redact_Replaces_an_iban()
    {
        var policy = new RedactionPolicy();

        var result = policy.Redact("IBAN: NL91ABNA0417164300 on file.");

        Assert.DoesNotContain("NL91ABNA0417164300", result, StringComparison.Ordinal);
        Assert.Contains(RedactionPolicy.Redacted, result, StringComparison.Ordinal);
    }

    [Fact]
    public void Redact_Replaces_a_phone_number()
    {
        var policy = new RedactionPolicy();

        var result = policy.Redact("Call +1 415-555-0134 now.");

        Assert.DoesNotContain("415-555-0134", result, StringComparison.Ordinal);
        Assert.Contains(RedactionPolicy.Redacted, result, StringComparison.Ordinal);
    }

    [Fact]
    public void Redact_Replaces_a_postal_code()
    {
        var policy = new RedactionPolicy();

        var result = policy.Redact("Ship to 94105-1234 please.");

        Assert.DoesNotContain("94105-1234", result, StringComparison.Ordinal);
        Assert.Contains(RedactionPolicy.Redacted, result, StringComparison.Ordinal);
    }

    [Fact]
    public void Redact_Replaces_a_secret_query_string_parameter()
    {
        var policy = new RedactionPolicy();

        var result = policy.Redact("https://api.example.test/data?api_key=abcd1234&format=json");

        Assert.DoesNotContain("abcd1234", result, StringComparison.Ordinal);
        Assert.Contains("format=json", result, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactValue_Redacts_a_pii_bearing_extracted_value_with_no_allow_list_entry()
    {
        // AC-OB-008: PII is redacted in logs when no allow-list entry exists; the returned payload itself
        // (not modeled here) is unaffected because RedactValue is only ever applied to the logging copy.
        var policy = new RedactionPolicy();

        var result = (string?)policy.RedactValue("value", "person@example.com", sourceId: "source-a", field: "email");

        Assert.DoesNotContain("person@example.com", result, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactValue_Does_not_redact_a_field_explicitly_allow_listed_for_pii()
    {
        // AC-OB-009: an allow-listed field's PII is not redacted in logs.
        var allowList = new PiiAllowList([("source-a", "email")]);
        var policy = new RedactionPolicy(allowList);

        var result = (string?)policy.RedactValue("value", "person@example.com", sourceId: "source-a", field: "email");

        Assert.Equal("person@example.com", result);
    }

    [Fact]
    public void RedactValue_Still_redacts_a_secret_query_value_even_for_an_allow_listed_field()
    {
        var allowList = new PiiAllowList([("source-a", "url")]);
        var policy = new RedactionPolicy(allowList);

        var result = (string?)policy.RedactValue("value", "https://x.test?api_key=shh", sourceId: "source-a", field: "url");

        Assert.DoesNotContain("shh", result, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactValue_Redacts_every_element_of_an_enumerable_value()
    {
        var policy = new RedactionPolicy();

        var result = policy.RedactValue("emails", new[] { "a@example.com", "b@example.com" });

        var redacted = Assert.IsAssignableFrom<object?[]>(result);
        Assert.All(redacted, item => Assert.Equal(RedactionPolicy.Redacted, item));
    }

    [Fact]
    public void RedactValue_Passes_through_a_null_value()
    {
        var policy = new RedactionPolicy();

        var result = policy.RedactValue("anything", null);

        Assert.Null(result);
    }

    [Fact]
    public void RedactingLogEnricher_Redacts_state_pairs_before_the_inner_logger_receives_them()
    {
        var captured = new List<KeyValuePair<string, object?>[]>();
        var inner = new CapturingLogger(captured);
        var enricher = new RedactingLogEnricher(inner);
        var state = new[] { new KeyValuePair<string, object?>("Authorization", "Bearer secret-value") };

        enricher.Log(LogLevel.Information, new EventId(1), state, null, (_, _) => "log message");

        Assert.Single(captured);
        Assert.DoesNotContain(captured[0], pair => Equals(pair.Value, "Bearer secret-value"));
    }

    [Fact]
    public void RedactingLogEnricher_Redacts_a_scope_state_string()
    {
        object? capturedScopeState = null;
        var inner = new CapturingLogger([], state => capturedScopeState = state);
        var enricher = new RedactingLogEnricher(inner);

        using var scope = enricher.BeginScope("contact person@example.com");

        var text = Assert.IsType<string>(capturedScopeState);
        Assert.DoesNotContain("person@example.com", text, StringComparison.Ordinal);
    }

    /// <summary>Minimal <see cref="ILogger"/> that records the redacted state it was given.</summary>
    private sealed class CapturingLogger(List<KeyValuePair<string, object?>[]> captured, Action<object?>? onScope = null) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            onScope?.Invoke(state);
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                captured.Add(pairs.ToArray());
            }
        }
    }
}
