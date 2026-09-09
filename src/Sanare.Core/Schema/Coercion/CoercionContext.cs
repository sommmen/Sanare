using System.Globalization;

namespace Sanare.Core.Schema.Coercion;

/// <summary>Context supplied by the extraction plan for a coercion operation.</summary>
public sealed record CoercionContext(
    CultureInfo? Culture = null,
    IReadOnlyDictionary<string, string>? EnumSynonyms = null,
    Uri? DocumentBaseUri = null,
    bool IsPresent = true)
{
    /// <summary>Gets the culture resolved for a field, falling back to invariant English.</summary>
    public CultureInfo ResolveCulture(FieldDescriptor field) =>
        Culture ?? CultureInfo.GetCultureInfo(field.Culture ?? "en-US");
}
