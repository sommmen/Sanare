namespace Sanare.Core.Schema.Coercion;

/// <summary>Converts normalised text values to the CLR types supported by the v0.1 engine.</summary>
public interface ITypeCoercer
{
    bool TryCoerce(string raw, FieldDescriptor field, out object? value, out string? error);
}
