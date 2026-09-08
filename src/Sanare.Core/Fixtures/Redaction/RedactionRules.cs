using System.Text.RegularExpressions;

namespace Sanare.Core.Fixtures.Redaction;

public static partial class RedactionRules
{
    [GeneratedRegex(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    public static partial Regex Email();
    [GeneratedRegex(@"\b[A-Z]{2}\d{2}[A-Z0-9]{11,30}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    public static partial Regex Iban();
    [GeneratedRegex(@"\b\d{4}\s?[A-Z]{2}\s+\d{1,5}[A-Z]?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    public static partial Regex DutchAddress();
    [GeneratedRegex("(?<=(?:password|token|apikey|api_key|secret|sessionid)[\\\"']?\\s*[:=]\\s*[\\\"'])[^\\\"']*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    public static partial Regex SensitiveValue();
    [GeneratedRegex("(<input\\b[^>]*\\b(?:name|id)=[\\\"']?(?:password|token|apikey|api_key|secret|sessionid)[^>]*\\bvalue=[\\\"'])[^\\\"']*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    public static partial Regex SensitiveInputValue();
}
