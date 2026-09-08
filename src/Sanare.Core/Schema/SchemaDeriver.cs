using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Sanare.Abstractions.Attributes;

namespace Sanare.Core.Schema;

/// <summary>Reflection-based v0.1 schema derivation for flat typed result models.</summary>
public sealed class SchemaDeriver : ISchemaDeriver
{
    private readonly NullabilityInfoContext _nullability = new();

    public SchemaDescriptor Derive<TSchema>(string defaultCulture = "en-US") where TSchema : class =>
        Derive(typeof(TSchema), defaultCulture);

    public SchemaDescriptor Derive(Type schemaType, string defaultCulture = "en-US")
    {
        ArgumentNullException.ThrowIfNull(schemaType);
        _ = CultureInfo.GetCultureInfo(defaultCulture);

        if (!schemaType.IsClass || schemaType.IsAbstract)
        {
            throw new InvalidOperationException("SNR-SCH-001: A schema must be a concrete class.");
        }

        var collectionProperties = schemaType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(static property => property.GetCustomAttribute<ScrapeCollectionAttribute>() is not null)
            .ToArray();
        if (collectionProperties.Length > 1)
        {
            throw new InvalidOperationException($"SNR-SCH-001: Schema '{schemaType.Name}' has multiple collection properties: {string.Join(", ", collectionProperties.Select(static property => property.Name))}.");
        }

        var typeField = schemaType.GetCustomAttribute<ScrapeFieldAttribute>();
        var typeCulture = schemaType.GetCustomAttribute<ScrapeCultureAttribute>()?.Culture ?? defaultCulture;
        var fields = schemaType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(static property => property.GetIndexParameters().Length == 0 && property.GetCustomAttribute<ScrapeIgnoreAttribute>() is null)
            .Select(property => ToDescriptor(property, typeCulture))
            .OrderBy(static field => field.JsonPointer, StringComparer.Ordinal)
            .ToArray();

        var name = schemaType.Name;
        var version = typeField?.Version ?? 1;
        var jsonSchema = SchemaHasher.CreateCanonicalJson(name, version, fields);
        var seed = new SchemaDescriptor(schemaType, name, version, jsonSchema, string.Empty, fields);
        return seed with { Hash = SchemaHasher.Compute(seed) };
    }

    private FieldDescriptor ToDescriptor(PropertyInfo property, string typeCulture)
    {
        var metadata = property.GetCustomAttribute<ScrapeFieldAttribute>();
        var nullable = _nullability.Create(property);
        var isNullableReference = !property.PropertyType.IsValueType && nullable.ReadState == NullabilityState.Nullable;
        var isNullableValue = Nullable.GetUnderlyingType(property.PropertyType) is not null;
        var requiredKeyword = property.GetCustomAttribute<RequiredMemberAttribute>() is not null;
        var required = requiredKeyword || metadata?.Required == true || !(isNullableReference || isNullableValue);
        var culture = property.GetCustomAttribute<ScrapeCultureAttribute>()?.Culture ?? typeCulture;
        _ = CultureInfo.GetCultureInfo(culture);

        return new FieldDescriptor(
            "/" + property.Name,
            property.Name,
            Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType,
            required,
            metadata?.Description,
            property.GetCustomAttribute<ScrapeUnitAttribute>()?.Unit,
            culture,
            property.GetCustomAttribute<ScrapeHintAttribute>()?.Hint);
    }
}
