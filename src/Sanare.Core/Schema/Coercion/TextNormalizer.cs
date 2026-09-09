using System.Net;
using System.Text;

namespace Sanare.Core.Schema.Coercion;

/// <summary>Applies the deterministic text normalisation required before comparison or coercion.</summary>
public static class TextNormalizer
{
    public static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var decoded = WebUtility.HtmlDecode(value);
        var builder = new StringBuilder(decoded.Length);
        var previousWhitespace = false;
        foreach (var character in decoded)
        {
            if (character is '\u00AD' or '\u200B' or '\u200C' or '\u200D' or '\uFEFF')
            {
                continue;
            }

            if (char.IsWhiteSpace(character) || character == '\u00A0')
            {
                if (!previousWhitespace)
                {
                    builder.Append(' ');
                }

                previousWhitespace = true;
                continue;
            }

            builder.Append(character);
            previousWhitespace = false;
        }

        return builder.ToString().Trim();
    }
}
