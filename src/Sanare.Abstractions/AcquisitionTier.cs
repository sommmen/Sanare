namespace Sanare.Abstractions;

/// <summary>
/// The acquisition strategy tier used to obtain content for a run, in order of preference.
/// Sanare always prefers the cheapest tier that can satisfy a source (see docs/sanare/tech-design.md §6).
/// </summary>
public enum AcquisitionTier
{
    /// <summary>Content was obtained from a first-party JSON API.</summary>
    JsonApi,

    /// <summary>Content was obtained via structured data embedded in an HTML page (JSON-LD, microdata, etc.).</summary>
    StructuredData,

    /// <summary>Content was obtained via a plain HTTP request and parsed as HTML.</summary>
    Html,

    /// <summary>Content was obtained by rendering the page in a browser.</summary>
    Browser,
}
