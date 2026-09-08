namespace Sanare.Core.Resolution;

/// <summary>
/// Identifies a request-side (sourceId, schemaName, schemaVersion) triple to resolve a plan for. See
/// docs/features/plan-resolver.md ("Inputs").
/// </summary>
/// <remarks>
/// The full spec keys the warm index by <c>(sourceId, schemaHash)</c>; this slice additionally carries
/// <c>SchemaName</c>/<c>SchemaVersion</c> because it has no authoring workflow to fall back on and must
/// locate the approval-tag namespace directly (<c>approved/{source-id}/{schema-name}@{schemaVersion}/{n}</c>).
/// </remarks>
public sealed record PlanResolutionRequest(
    string SourceId,
    string SchemaName,
    int SchemaVersion,
    string SchemaHash);
