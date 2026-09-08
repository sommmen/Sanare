namespace Sanare.Abstractions.Attributes;

/// <summary>
/// Free-text guidance attached to a field for the benefit of a future plan-authoring agent (e.g.
/// "usually the second table row"). Never interpreted by the runtime; purely descriptive metadata.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class ScrapeHintAttribute(string hint) : Attribute
{
    /// <summary>The free-text hint.</summary>
    public string Hint { get; } = hint;
}
