using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sanare.Core.Schema.Coercion;

/// <summary>Explicit, culture-aware text coercion for all supported schema field shapes.</summary>
public sealed class TypeCoercer : ITypeCoercer
{
    public CoercionOutcome Coerce(string? raw, FieldDescriptor field, CoercionContext context)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(context);
        if (raw is null || TextNormalizer.Normalize(raw).Length == 0)
        {
            return field.Required
                ? Failure(raw, "SNR-SCH-004: A required value is missing.")
                : new CoercionOutcome(true, null, raw, null, null);
        }

        var text = TextNormalizer.Normalize(raw);
        var culture = context.ResolveCulture(field);
        if (field.Hint is not null && string.Equals(field.Hint, "presence", StringComparison.OrdinalIgnoreCase))
        {
            return context.IsPresent
                ? Success(true, raw, null)
                : Failure(raw, "SNR-SCH-004: A required value is missing.");
        }

        var isNumeric = IsNumeric(field.ClrType);
        var unit = isNumeric ? ExtractTrailingUnit(ref text, field.Unit) : null;
        if (field.Unit is { Length: > 0 } && unit is null && isNumeric)
        {
            return Failure(raw, $"SNR-SCH-005: Expected unit '{field.Unit}' for '{field.ClrType.Name}'.");
        }

        if (field.Unit is { Length: > 0 } && unit is not null &&
            !string.Equals(unit, field.Unit, StringComparison.OrdinalIgnoreCase) &&
            !UnitConverter.TryConvert(0m, unit, field.Unit, out _))
        {
            return Failure(raw, $"SNR-SCH-005: Expected unit '{field.Unit}' for '{field.ClrType.Name}'.");
        }

        if (field.ClrType == typeof(string))
        {
            return Success(text, raw, unit);
        }

        if (TryConvertDictionary(text, field, out var dictionary))
        {
            return dictionary;
        }

        if (TryConvertCollection(text, field, context, out var collection))
        {
            return collection;
        }

        if (!TryConvertScalar(text, field.ClrType, culture, context, field.EnumSynonyms, out var converted))
        {
            return Failure(raw, $"SNR-SCH-005: Cannot coerce value to '{field.ClrType.Name}'.");
        }

        if (isNumeric && unit is not null && field.Unit is not null &&
            !string.Equals(unit, field.Unit, StringComparison.OrdinalIgnoreCase))
        {
            if (!TryConvertNumericUnit(converted, field.ClrType, unit, field.Unit, out converted))
            {
                return Failure(raw, $"SNR-SCH-005: Cannot convert unit '{unit}' to '{field.Unit}'.");
            }
        }

        return Success(converted, raw, field.Unit ?? unit);
    }

    public bool TryCoerce(string raw, FieldDescriptor field, out object? value, out string? error)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(field);
        var outcome = Coerce(raw, field, new CoercionContext());
        value = outcome.Success && outcome.Value is not null ? ToClrValue(outcome.Value, field.ClrType) : null;
        error = outcome.FailureReason;
        return outcome.Success;
    }

    private static bool TryGetDecimal(object? value, out decimal numeric)
    {
        switch (value)
        {
            case int intValue:
                numeric = intValue;
                return true;
            case long longValue:
                numeric = longValue;
                return true;
            case decimal decimalValue:
                numeric = decimalValue;
                return true;
            case double doubleValue when double.IsFinite(doubleValue):
                try
                {
                    numeric = (decimal)doubleValue;
                    return true;
                }
                catch (OverflowException)
                {
                    break;
                }
            case float floatValue when float.IsFinite(floatValue):
                try
                {
                    numeric = (decimal)floatValue;
                    return true;
                }
                catch (OverflowException)
                {
                    break;
                }
        }

        numeric = default;
        return false;
    }

    private static bool TryConvertNumericUnit(object? value, Type targetType, string sourceUnit, string targetUnit, out object? converted)
    {
        if (!TryGetDecimal(value, out var numeric))
        {
            converted = null;
            return false;
        }

        if (!UnitConverter.TryConvert(numeric, sourceUnit, targetUnit, out var convertedNumeric))
        {
            converted = null;
            return false;
        }

        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (underlyingType == typeof(int))
        {
            if (decimal.Truncate(convertedNumeric) != convertedNumeric || convertedNumeric < int.MinValue || convertedNumeric > int.MaxValue)
            {
                converted = null;
                return false;
            }

            converted = (int)convertedNumeric;
            return true;
        }

        if (underlyingType == typeof(long))
        {
            if (decimal.Truncate(convertedNumeric) != convertedNumeric || convertedNumeric < long.MinValue || convertedNumeric > long.MaxValue)
            {
                converted = null;
                return false;
            }

            converted = (long)convertedNumeric;
            return true;
        }

        if (underlyingType == typeof(decimal))
        {
            converted = convertedNumeric;
            return true;
        }

        if (underlyingType == typeof(double))
        {
            var doubleValue = (double)convertedNumeric;
            converted = double.IsFinite(doubleValue) ? doubleValue : null;
            return converted is not null;
        }

        if (underlyingType == typeof(float))
        {
            var floatValue = (float)convertedNumeric;
            converted = float.IsFinite(floatValue) ? floatValue : null;
            return converted is not null;
        }

        converted = null;
        return false;
    }

    private static bool TryConvertCollection(string text, FieldDescriptor field, CoercionContext context, out CoercionOutcome outcome)
    {
        var type = field.ClrType;
        var elementType = type.IsArray ? type.GetElementType() :
            type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>) ? type.GetGenericArguments()[0] : null;
        if (elementType is null)
        {
            outcome = default;
            return false;
        }

        var items = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var array = new JsonArray();
        foreach (var item in items)
        {
            if (!TryConvertScalar(item, elementType, context.ResolveCulture(field), context, field.EnumSynonyms, out var value))
            {
                outcome = Failure(text, $"SNR-SCH-005: Cannot coerce collection element to '{elementType.Name}'.");
                return true;
            }

            array.Add(ToJsonNode(value));
        }

        outcome = new CoercionOutcome(true, array, text, null, null);
        return true;
    }

    private static bool TryConvertDictionary(string text, FieldDescriptor field, out CoercionOutcome outcome)
    {
        if (!field.ClrType.IsGenericType ||
            field.ClrType.GetGenericTypeDefinition() != typeof(IReadOnlyDictionary<,>) ||
            field.ClrType.GetGenericArguments() is not [var keyType, var valueType] ||
            keyType != typeof(string) || valueType != typeof(string))
        {
            outcome = default;
            return false;
        }

        var dictionary = new JsonObject();
        foreach (var pair in text.Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = pair.IndexOf(':');
            if (separator <= 0)
            {
                outcome = Failure(text, "SNR-SCH-005: Dictionary entries must contain a label and value separated by ':'.");
                return true;
            }

            var key = TextNormalizer.Normalize(pair[..separator]);
            var value = TextNormalizer.Normalize(pair[(separator + 1)..]);
            var uniqueKey = key;
            for (var suffix = 2; dictionary.ContainsKey(uniqueKey); suffix++)
            {
                uniqueKey = $"{key}#{suffix}";
            }

            dictionary[uniqueKey] = value;
        }

        outcome = new CoercionOutcome(true, dictionary, text, null, null);
        return true;
    }

    private static bool TryConvertScalar(
        string text,
        Type type,
        CultureInfo culture,
        CoercionContext context,
        IReadOnlyList<string>? fieldSynonyms,
        out object? value)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(int) && int.TryParse(text, NumberStyles.Integer | NumberStyles.AllowThousands, culture, out var integer))
        {
            value = integer;
            return true;
        }

        if (type == typeof(long) && long.TryParse(text, NumberStyles.Integer | NumberStyles.AllowThousands, culture, out var longValue))
        {
            value = longValue;
            return true;
        }

        if (type == typeof(decimal) && decimal.TryParse(text, NumberStyles.Number | NumberStyles.AllowCurrencySymbol, culture, out var decimalValue))
        {
            value = decimalValue;
            return true;
        }

        if (type == typeof(double) && double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, culture, out var doubleValue))
        {
            value = doubleValue;
            return true;
        }

        if (type == typeof(float) && float.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, culture, out var floatValue))
        {
            value = floatValue;
            return true;
        }

        if (type == typeof(bool) && CultureTruthTable.TryParse(text, culture, out var boolean))
        {
            value = boolean;
            return true;
        }

        if (type == typeof(DateTime) && TryParseDateTime(text, culture, out var dateTime))
        {
            value = dateTime;
            return true;
        }

        if (type == typeof(DateOnly) && TryParseDateOnly(text, culture, out var dateOnly))
        {
            value = dateOnly;
            return true;
        }

        if (type == typeof(Uri) && Uri.TryCreate(context.DocumentBaseUri, text, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            value = uri;
            return true;
        }

        if (type.IsEnum && TryParseEnum(text, type, MergeSynonyms(context.EnumSynonyms, fieldSynonyms), out var enumValue))
        {
            value = enumValue;
            return true;
        }

        if (type == typeof(string))
        {
            value = text;
            return true;
        }

        value = null;
        return false;
    }

    private static string? ExtractTrailingUnit(ref string text, string? declaredUnit)
    {
        if (declaredUnit is { Length: > 0 } && text.EndsWith(declaredUnit, StringComparison.OrdinalIgnoreCase))
        {
            text = TextNormalizer.Normalize(text[..^declaredUnit.Length]);
            return declaredUnit;
        }

        var separator = text.LastIndexOf(' ');
        if (separator > 0 && separator < text.Length - 1)
        {
            var possibleUnit = text[(separator + 1)..];
            if (possibleUnit.All(static character => char.IsLetter(character) || character is '/' or '²'))
            {
                text = text[..separator];
                return possibleUnit;
            }
        }

        return null;
    }

    private static bool TryParseDateTime(string text, CultureInfo culture, out DateTime value)
    {
        // Try invariant culture first with RoundtripKind to preserve explicit timezone info (Z, +hh:mm, etc.)
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value))
        {
            return true;
        }

        // Fall back to culture-specific parsing; if no explicit offset/zone in input,
        // treat as UTC rather than reinterpreting through machine's local timezone
        if (DateTime.TryParse(text, culture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
        {
            // If parsed DateTime has no explicit timezone info (Kind is Unspecified or Local),
            // treat it as already representing UTC instead of converting via ToUniversalTime()
            value = parsed.Kind switch
            {
                DateTimeKind.Unspecified => DateTime.SpecifyKind(parsed, DateTimeKind.Utc),
                DateTimeKind.Local => parsed.ToUniversalTime(),
                DateTimeKind.Utc => parsed,
                _ => parsed
            };
            return true;
        }

        return false;
    }

    private static bool TryParseDateOnly(string text, CultureInfo culture, out DateOnly value) =>
        DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out value) ||
        DateOnly.TryParse(text, culture, DateTimeStyles.AllowWhiteSpaces, out value);

    private static IReadOnlyDictionary<string, string>? MergeSynonyms(
        IReadOnlyDictionary<string, string>? contextSynonyms,
        IReadOnlyList<string>? fieldSynonyms)
    {
        if (fieldSynonyms is null || fieldSynonyms.Count == 0)
        {
            return contextSynonyms;
        }

        var merged = contextSynonyms is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(contextSynonyms, StringComparer.OrdinalIgnoreCase);
        foreach (var synonym in fieldSynonyms)
        {
            var separator = synonym.IndexOf('=');
            if (separator > 0 && separator < synonym.Length - 1)
            {
                merged[synonym[..separator].Trim()] = synonym[(separator + 1)..].Trim();
            }
        }

        return merged;
    }

    private static bool TryParseEnum(string text, Type type, IReadOnlyDictionary<string, string>? synonyms, out object? value)
    {
        foreach (var name in Enum.GetNames(type))
        {
            var member = type.GetMember(name, BindingFlags.Public | BindingFlags.Static)[0];
            var description = member.GetCustomAttribute<DescriptionAttribute>()?.Description;
            if (string.Equals(text, name, StringComparison.OrdinalIgnoreCase) || string.Equals(text, description, StringComparison.OrdinalIgnoreCase) ||
                (synonyms is not null && synonyms.TryGetValue(text, out var synonym) && string.Equals(synonym, name, StringComparison.OrdinalIgnoreCase)))
            {
                value = Enum.Parse(type, name);
                return true;
            }
        }

        value = null;
        return false;
    }

    private static CoercionOutcome Success(object? value, string raw, string? unit) =>
        new(true, ToJsonNode(value), raw, unit, null);

    private static CoercionOutcome Failure(string? raw, string reason) =>
        new(false, null, raw, null, reason);

    private static JsonNode? ToJsonNode(object? value) => value switch
    {
        null => null,
        Uri uri => JsonValue.Create(uri.ToString()),
        DateOnly date => JsonValue.Create(date.ToString("O", CultureInfo.InvariantCulture)),
        DateTime dateTime => JsonValue.Create(dateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
        _ => JsonSerializer.SerializeToNode(value),
    };

    private static object? ToClrValue(JsonNode node, Type type)
    {
        if (type == typeof(Uri) && node.GetValue<string>() is { } uriText)
        {
            return new Uri(uriText, UriKind.Absolute);
        }

        return node.Deserialize(type);
    }

    private static bool IsNumeric(Type type)
    {
        // Unwrap Nullable<T> to check the underlying type
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
        return underlyingType == typeof(int) || underlyingType == typeof(long) || underlyingType == typeof(decimal) ||
            underlyingType == typeof(double) || underlyingType == typeof(float);
    }
}
