namespace Sanare.Core.Acquisition.Content;

/// <summary>
/// Enforces the per-request content-type allow-list (docs/sanare/tech-design.md §7.4,
/// docs/features/acquisition-pipeline.md, "Key Behaviors" &gt; "Safety limits"). The gate runs
/// <em>after</em> fixture capture so that an unexpected body is still preserved for diagnosis.
/// </summary>
public static class ContentTypeGate
{
    /// <summary>Throws <c>SNR-ACQ-006</c> when <paramref name="contentType"/> is outside <paramref name="expected"/>.</summary>
    /// <param name="contentType">The response media type, without parameters.</param>
    /// <param name="expected">The allow-list for this request.</param>
    public static void Validate(string contentType, IReadOnlySet<string> expected)
    {
        ArgumentNullException.ThrowIfNull(contentType);
        ArgumentNullException.ThrowIfNull(expected);

        if (!expected.Contains(contentType))
        {
            throw new AcquisitionException("SNR-ACQ-006", $"Unexpected response content type '{contentType}'.");
        }
    }

    /// <summary>Returns whether <paramref name="contentType"/> is inside <paramref name="expected"/>, without throwing.</summary>
    /// <param name="contentType">The response media type, without parameters.</param>
    /// <param name="expected">The allow-list for this request.</param>
    public static bool IsAllowed(string contentType, IReadOnlySet<string> expected)
    {
        ArgumentNullException.ThrowIfNull(contentType);
        ArgumentNullException.ThrowIfNull(expected);
        return expected.Contains(contentType);
    }
}
