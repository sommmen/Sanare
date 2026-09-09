using System.Globalization;

namespace Sanare.Core.Schema.Coercion;

/// <summary>Deterministic truthy and falsy vocabulary used for culture-aware Boolean coercion.</summary>
public static class CultureTruthTable
{
    private static readonly IReadOnlyDictionary<string, bool> Shared = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
    {
        ["true"] = true,
        ["false"] = false,
        ["yes"] = true,
        ["no"] = false,
        ["✓"] = true,
        ["✗"] = false,
        ["1"] = true,
        ["0"] = false,
    };

    public static bool TryParse(string text, CultureInfo culture, out bool value)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(culture);
        if (Shared.TryGetValue(text, out value))
        {
            return true;
        }

        if (culture.Name.StartsWith("nl", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(text, "ja", StringComparison.OrdinalIgnoreCase))
            {
                value = true;
                return true;
            }

            if (string.Equals(text, "nee", StringComparison.OrdinalIgnoreCase))
            {
                value = false;
                return true;
            }
        }

        value = default;
        return false;
    }
}
