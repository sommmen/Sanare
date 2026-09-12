namespace Sanare.Abstractions;

/// <summary>
/// Where the content backing a <see cref="ScrapeResult{T}"/> ultimately came from.
/// </summary>
public enum ResultOrigin
{
    /// <summary>Fetched live over the network.</summary>
    Network,

    /// <summary>Served from the HTTP-level cache.</summary>
    HttpCache,

    /// <summary>Served from the result-level cache.</summary>
    ResultCache,

    /// <summary>Served from a recorded fixture (no network access).</summary>
    Fixture,

    /// <summary>Fetched via the browser tier (headless rendering).</summary>
    Browser,
}
