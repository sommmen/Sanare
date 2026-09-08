using Sanare.Abstractions.Plans;

namespace Sanare.Core.Plans;

/// <summary>
/// Reads and writes <see cref="ExtractionPlan"/> documents as canonical, byte-stable JSON so that
/// git history for a plan reflects real semantic changes only. See
/// docs/features/extraction-plan-model.md ("Canonical serialization").
/// </summary>
public interface IPlanSerializer
{
    /// <summary>Deserializes an <see cref="ExtractionPlan"/> from its canonical (or tolerant) JSON text.</summary>
    ExtractionPlan Read(string json);

    /// <summary>Serializes <paramref name="plan"/> to deterministic, byte-stable canonical JSON.</summary>
    string WriteCanonical(ExtractionPlan plan);
}
