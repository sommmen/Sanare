using Sanare.Abstractions.Diagnostics;

namespace Sanare.Abstractions;

/// <summary>
/// Thrown by <see cref="IScrapeRunner.StreamAsync{TItem}"/> to surface a terminal failure. An
/// <see cref="IAsyncEnumerable{T}"/> has no result envelope to carry a status in, so this exception
/// carries the same <see cref="ScrapeStatus"/> and <see cref="Diagnostics"/> that
/// <see cref="IScrapeRunner.RunAsync{TSchema}"/> would have returned for the equivalent request.
/// </summary>
public sealed class ScrapeStreamException : Exception
{
    /// <summary>The terminal status that ended the stream.</summary>
    public ScrapeStatus Status { get; }

    /// <summary>Diagnostics accumulated up to the point of failure.</summary>
    public IReadOnlyList<ScrapeDiagnostic> Diagnostics { get; }

    public ScrapeStreamException(ScrapeStatus status, IReadOnlyList<ScrapeDiagnostic> diagnostics)
        : base(BuildMessage(status, diagnostics))
    {
        Status = status;
        Diagnostics = diagnostics;
    }

    public ScrapeStreamException(ScrapeStatus status, IReadOnlyList<ScrapeDiagnostic> diagnostics, Exception innerException)
        : base(BuildMessage(status, diagnostics), innerException)
    {
        Status = status;
        Diagnostics = diagnostics;
    }

    private static string BuildMessage(ScrapeStatus status, IReadOnlyList<ScrapeDiagnostic> diagnostics)
    {
        var first = diagnostics.Count > 0 ? diagnostics[0].Message : null;
        return first is null
            ? $"Stream ended with status {status}."
            : $"Stream ended with status {status}: {first}";
    }
}
