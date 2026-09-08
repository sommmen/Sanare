using System.Globalization;

namespace Sanare.Core.Schema.Coercion;

/// <summary>Explicit, culture-aware text coercion for the v0.1 scalar schema types.</summary>
public sealed class TypeCoercer : ITypeCoercer
{
    public bool TryCoerce(string raw, FieldDescriptor field, out object? value, out string? error)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(field);

        var text = TextNormalizer.Normalize(raw);
        if (field.Unit is { Length: > 0 })
        {
            if (!text.EndsWith(field.Unit, StringComparison.OrdinalIgnoreCase))
            {
                value = null;
                error = $"Expected unit '{field.Unit}'.";
                return false;
            }

            text = TextNormalizer.Normalize(text[..^field.Unit.Length]);
        }

        var culture = CultureInfo.GetCultureInfo(field.Culture);
        var type = field.ClrType;
        if (type == typeof(string))
        {
            value = text;
            error = null;
            return true;
        }

        if (type == typeof(int) && int.TryParse(text, NumberStyles.Integer | NumberStyles.AllowThousands, culture, out var integer))
        {
            value = integer;
            error = null;
            return true;
        }

        if (type == typeof(decimal) && decimal.TryParse(text, NumberStyles.Number, culture, out var decimalValue))
        {
            value = decimalValue;
            error = null;
            return true;
        }

        if (type == typeof(bool) && bool.TryParse(text, out var boolean))
        {
            value = boolean;
            error = null;
            return true;
        }

        value = null;
        error = $"Cannot coerce '{text}' to '{type.Name}' using culture '{culture.Name}'.";
        return false;
    }
}
