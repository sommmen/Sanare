namespace Sanare.Abstractions.Plans;

/// <summary>
/// The HTTP method used to fetch a plan's acquisition URL. See
/// docs/sanare/tech-design.md §10.1.1 for the canonical plan document shape (only <see cref="Get"/>
/// appears in the documented example; <see cref="Post"/> is included so a JSON API that requires a
/// request body — e.g. a search or GraphQL-style endpoint — is not structurally impossible to describe.
/// Only the type shape is owned by v0.1; issuing the request is <c>acquisition-pipeline</c>/<c>plan-runtime</c>
/// scope (M2) and not implemented yet.
/// </summary>
public enum AcquisitionMethod
{
    /// <summary>Fetch the URL with an HTTP GET request.</summary>
    Get,

    /// <summary>Fetch the URL with an HTTP POST request.</summary>
    Post,
}
