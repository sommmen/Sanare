namespace Sanare.Http.Identity;

/// <summary>
/// How the request currently being composed relates to the run's navigation so far. Drives
/// <c>Sec-Fetch-*</c> header values and whether a <c>Referer</c> may be emitted
/// (docs/features/browsing-identity.md, "Key Behaviors", coherence rule 5).
/// </summary>
public enum NavigationContext
{
    /// <summary>A fresh entry point into a source (a listing page, a search result, a homepage).</summary>
    TopLevel,

    /// <summary>A sub-resource fetched from the same origin as the page that referenced it.</summary>
    SameOriginSubResource,

    /// <summary>A detail page reached by following a link from a lister page within the same run.</summary>
    DetailFromLister,
}
