using System.Text;

namespace Sanare.Core.Schema.Coercion;

/// <summary>Applies the deterministic text normalisation required before comparison or coercion.</summary>
public static class TextNormalizer
{
    public static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var cleaned = value.Replace("\u00AD", string.Empty, StringComparison.Ordinal)
            .Replace("\u200B", string.Empty, StringComparison.Ordinal);
        var builder = new StringBuilder(cleaned.Length);
        var previousWhitespace = false;
        foreach (var character in cleaned)
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWhitespace)
                {
                    builder.Append(' ');
                }

                previousWhitespace = true;
            }
            else
            {
                builder.Append(character);
                previousWhitespace = false;
            }
        }

        return builder.ToString().Trim();
    }
}
