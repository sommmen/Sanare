namespace Sanare.Core.Schema.Coercion;

/// <summary>Converts normalised text values to the CLR types supported by the v0.1 engine.</summary>
public interface ITypeCoercer
{
    /// <summary>Coerces a raw field value with plan-provided culture and source context.</summary>
    CoercionOutcome Coerce(string? raw, FieldDescriptor field, CoercionContext context);

    /// <summary>Coerces separately extracted raw values for a field.</summary>
    CoercionOutcome Coerce(IReadOnlyList<string> rawValues, FieldDescriptor field, CoercionContext context);

    /// <summary>Compatibility conversion method for legacy plan execution.</summary>
    bool TryCoerce(string raw, FieldDescriptor field, out object? value, out string? error);
}
