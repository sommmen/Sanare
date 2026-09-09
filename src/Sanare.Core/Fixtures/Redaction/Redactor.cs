using System.Text;

namespace Sanare.Core.Fixtures.Redaction;

public sealed class Redactor : IRedactor
{
    public RedactionResult Redact(ReadOnlySpan<byte> content, IReadOnlyDictionary<string, string>? headers = null)
    {
        var rules = new List<string>();
        var safeHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (headers is not null)
        {
            foreach (var header in headers)
            {
                if (header.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
                {
                    AddRule(rules, "cookie");
                    continue;
                }
                if (header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) || header.Key.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase))
                {
                    AddRule(rules, "credential");
                    continue;
                }
                safeHeaders[header.Key] = header.Value;
            }
        }

        var text = Encoding.UTF8.GetString(content);
        text = Replace(RedactionRules.SensitiveInputValue(), text, "$1[REDACTED]", "credential", rules);
        text = Replace(RedactionRules.SensitiveValue(), text, "[REDACTED]", "credential", rules);
        text = Replace(RedactionRules.Email(), text, "redacted@example.invalid", "email", rules);
        text = Replace(RedactionRules.Iban(), text, "[REDACTED]", "iban", rules);
        text = Replace(RedactionRules.DutchAddress(), text, "[REDACTED]", "pii", rules);
        return new RedactionResult(Encoding.UTF8.GetBytes(text), rules, safeHeaders);
    }

    private static string Replace(System.Text.RegularExpressions.Regex expression, string input, string replacement, string rule, List<string> rules)
    {
        if (!expression.IsMatch(input)) { return input; }
        AddRule(rules, rule);
        return expression.Replace(input, replacement);
    }

    private static void AddRule(List<string> rules, string rule)
    {
        if (!rules.Contains(rule, StringComparer.Ordinal)) { rules.Add(rule); }
    }
}
