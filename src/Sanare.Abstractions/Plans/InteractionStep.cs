namespace Sanare.Abstractions.Plans;

/// <summary>
/// A single browser-tier interaction step (e.g. click, wait, type) executed before extraction begins.
/// See docs/sanare/tech-design.md §10.1.1 (<c>acquisition.interactions</c>) and
/// docs/features/extraction-plan-model.md rule 3 — every <see cref="Operation"/> here must be one of the
/// six browser-only <see cref="PlanOperation"/> members, enforced by <c>IPlanValidator</c>.
/// </summary>
/// <remarks>
/// Shares its shape with <see cref="LocatorStep"/>/<see cref="TransformStep"/> deliberately: the plan
/// model has exactly one way to name an operation and its arguments. This record carries no behaviour;
/// executing an interaction is <c>plan-runtime</c> scope (M2) and not implemented in v0.1.
/// </remarks>
public sealed record InteractionStep(PlanOperation Operation, IReadOnlyList<string> Arguments);
