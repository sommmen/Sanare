namespace Sanare.Abstractions.Diagnostics;

/// <summary>
/// Applies the sanitisation rules shared by every component that raises <see cref="ScrapeDiagnostic"/>s:
/// <c>Message</c> must never contain credentials, cookie values, query-string values, or raw page
/// content, and <c>Detail</c> is only surfaced to consumers when explicitly opted into.
/// </summary>
public static class DiagnosticSanitizer
{
    /// <summary>
    /// Returns <paramref name="diagnostic"/> unchanged when <paramref name="includeDetail"/> is
    /// <see langword="true"/>; otherwise returns a copy with <see cref="ScrapeDiagnostic.Detail"/>
    /// cleared. <see cref="ScrapeDiagnostic.Message"/> is never altered by this step, since callers are
    /// expected to have already produced a sanitised message (see <see cref="SanitizeUrl"/>).
    /// </summary>
    public static ScrapeDiagnostic Sanitize(ScrapeDiagnostic diagnostic, bool includeDetail)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        return includeDetail ? diagnostic : diagnostic with { Detail = null };
    }

    /// <summary>
    /// Reduces a URL to scheme + host + path for safe inclusion in a diagnostic <c>Message</c>,
    /// dropping userinfo, query string, and fragment.
    /// </summary>
    public static string SanitizeUrl(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}";
    }
}
