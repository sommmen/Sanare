using System.Text.RegularExpressions;

namespace Sanare.Core.Observability.Redaction;

/// <summary>Compiled patterns for secrets and configured PII classes.</summary>
public static partial class RedactionPatterns
{
    [GeneratedRegex(@"(?i)([?&](?:token|api[_-]?key|secret|password|access[_-]?token)\s*=\s*)[^&#\s]+")]
    public static partial Regex SecretQueryValue();

    [GeneratedRegex(@"(?<![\w.@+-])[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}")]
    public static partial Regex Email();

    [GeneratedRegex(@"(?<!\w)(?:\+?\d[\d ()-]{7,}\d)(?!\w)")]
    public static partial Regex Phone();

    [GeneratedRegex(@"\b[A-Z]{2}\d{2}[A-Z0-9]{11,30}\b", RegexOptions.IgnoreCase)]
    public static partial Regex Iban();

    [GeneratedRegex(@"\b\d{5}(?:-\d{4})?\b")]
    public static partial Regex PostalCode();
}
