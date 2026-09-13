using System.Text;
using System.Text.RegularExpressions;
using Sanare.Abstractions.Diagnostics;

namespace Sanare.Core.Acquisition.Content;

/// <summary>
/// The resolved character encoding for a response body plus, when nothing declared one, the warning
/// that records the silent UTF-8 fallback (docs/features/acquisition-pipeline.md AC-ACQ-017).
/// </summary>
/// <param name="Encoding">The encoding to decode the body with.</param>
/// <param name="Source">Which signal decided the encoding.</param>
/// <param name="Warning">Populated only when <see cref="Source"/> is <see cref="CharsetSource.Fallback"/>.</param>
public sealed record CharsetResolution(Encoding Encoding, CharsetSource Source, ScrapeDiagnostic? Warning);

/// <summary>Which signal decided a response's character encoding.</summary>
public enum CharsetSource
{
    /// <summary>The <c>Content-Type</c> header carried an explicit, usable <c>charset</c> parameter.</summary>
    Header,

    /// <summary>The body opened with a recognised byte-order mark.</summary>
    ByteOrderMark,

    /// <summary>An HTML <c>&lt;meta&gt;</c> declaration inside the first 8 KiB named a usable encoding.</summary>
    MetaTag,

    /// <summary>Nothing declared an encoding, so UTF-8 was assumed. Always accompanied by a warning.</summary>
    Fallback,
}

/// <summary>
/// Resolves a response body's character encoding in the documented precedence order: <c>Content-Type</c>
/// charset, then byte-order mark, then an HTML <c>&lt;meta&gt;</c> declaration inside the first 8 KiB, then
/// UTF-8. The final step is a guess, so it is reported rather than applied silently.
/// </summary>
public static partial class CharsetDetector
{
    private const int MetaSampleBytes = 8192;
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];
    private static readonly byte[] Utf16LeBom = [0xFF, 0xFE];
    private static readonly byte[] Utf16BeBom = [0xFE, 0xFF];

    [GeneratedRegex("<meta\\s+[^>]*charset\\s*=\\s*[\"']?\\s*([^\\s\"'/>;]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MetaCharsetPattern();

    [GeneratedRegex("<meta\\s+[^>]*content\\s*=\\s*[\"'][^>]*charset\\s*=\\s*([^\\s\"';>]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MetaContentCharsetPattern();

    /// <summary>Resolves the encoding for <paramref name="body"/>, given the declared header charset if any.</summary>
    /// <param name="headerCharset">The <c>charset</c> parameter from the <c>Content-Type</c> header, or <see langword="null"/>.</param>
    /// <param name="body">The response body, or at least its first 8 KiB.</param>
    public static CharsetResolution Resolve(string? headerCharset, ReadOnlySpan<byte> body)
    {
        if (TryGetEncoding(headerCharset, out var headerEncoding))
        {
            return new CharsetResolution(headerEncoding, CharsetSource.Header, Warning: null);
        }

        if (body.StartsWith(Utf8Bom))
        {
            return new CharsetResolution(new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), CharsetSource.ByteOrderMark, Warning: null);
        }

        if (body.StartsWith(Utf16LeBom))
        {
            return new CharsetResolution(Encoding.Unicode, CharsetSource.ByteOrderMark, Warning: null);
        }

        if (body.StartsWith(Utf16BeBom))
        {
            return new CharsetResolution(Encoding.BigEndianUnicode, CharsetSource.ByteOrderMark, Warning: null);
        }

        var sample = Encoding.ASCII.GetString(body[..Math.Min(body.Length, MetaSampleBytes)]);
        var match = MetaCharsetPattern().Match(sample);
        if (!match.Success)
        {
            match = MetaContentCharsetPattern().Match(sample);
        }

        if (match.Success && TryGetEncoding(match.Groups[1].Value, out var metaEncoding))
        {
            return new CharsetResolution(metaEncoding, CharsetSource.MetaTag, Warning: null);
        }

        var warning = new ScrapeDiagnostic(
            "SNR-ACQ-014",
            DiagnosticSeverity.Warning,
            "No character encoding was declared by the response; assuming UTF-8.");
        return new CharsetResolution(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), CharsetSource.Fallback, warning);
    }

    private static bool TryGetEncoding(string? name, out Encoding encoding)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            try
            {
                encoding = Encoding.GetEncoding(name.Trim().Trim('"', '\''));
                return true;
            }
            catch (ArgumentException)
            {
                // An unknown or malformed charset name falls through to the next signal.
            }
        }

        encoding = null!;
        return false;
    }
}
