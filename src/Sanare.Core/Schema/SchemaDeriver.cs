using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Sanare.Abstractions.Attributes;

namespace Sanare.Core.Schema;

/// <summary>Derives deterministic metadata from supported typed result models.</summary>
public sealed class SchemaDeriver : ISchemaDeriver
{
    private static readonly ConcurrentDictionary<CacheKey, SchemaDescriptor> Cache = new();
    private readonly NullabilityInfoContext _nullability = new();

    public SchemaDescriptor Derive<TSchema>(string defaultCulture = "en-US") where TSchema : class => Derive(typeof(TSchema), defaultCulture);

    public SchemaDescriptor Derive(Type schemaType, string defaultCulture = "en-US")
    {
        ArgumentNullException.ThrowIfNull(schemaType);
        _ = CultureInfo.GetCultureInfo(defaultCulture);
        if (!schemaType.IsClass || schemaType.IsAbstract)
        {
            throw new InvalidOperationException("SNR-SCH-001: A schema must be a concrete class.");
        }

        return Cache.GetOrAdd(new CacheKey(schemaType, defaultCulture), static (key, self) => self.DeriveCore(key.Type, key.Culture), this);
    }

    private SchemaDescriptor DeriveCore(Type schemaType, string defaultCulture)
    {
        var typeField = schemaType.GetCustomAttribute<ScrapeFieldAttribute>();
        var culture = schemaType.GetCustomAttribute<ScrapeCultureAttribute>()?.Culture ?? defaultCulture;
        var fields = new List<FieldDescriptor>();
        string? collectionPointer = null;
        Visit(schemaType, string.Empty, culture, 0, new HashSet<Type>(), fields, ref collectionPointer);
        var ordered = fields.OrderBy(static x => x.JsonPointer, StringComparer.Ordinal).ToArray();
        var json = SchemaHasher.CreateCanonicalJson(schemaType.Name, typeField?.Version ?? 1, ordered);
        var descriptor = new SchemaDescriptor(schemaType, schemaType.Name, typeField?.Version ?? 1, json, string.Empty, ordered, collectionPointer);
        return descriptor with { Hash = SchemaHasher.Compute(descriptor) };
    }

    private void Visit(Type type, string prefix, string inheritedCulture, int depth, HashSet<Type> ancestry, ICollection<FieldDescriptor> fields, ref string? collectionPointer)
    {
        if (depth > 8)
        {
            throw InvalidShape($"depth exceeds 8 at '{prefix}'");
        }
        if (!ancestry.Add(type))
        {
            throw InvalidShape($"type cycle at '{prefix}'");
        }

        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(static p => p.GetMethod is not null && p.GetIndexParameters().Length == 0).OrderBy(static p => p.Name, StringComparer.Ordinal))
        {
            if (property.GetCustomAttribute<ScrapeIgnoreAttribute>() is not null)
            {
                continue;
            }

            var pointer = prefix + "/" + EscapePointer(property.Name);
            var propertyCulture = property.GetCustomAttribute<ScrapeCultureAttribute>()?.Culture ?? inheritedCulture;
            var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            var collection = property.GetCustomAttribute<ScrapeCollectionAttribute>() is not null;
            if (collection)
            {
                if (collectionPointer is not null)
                {
                    throw InvalidShape($"multiple collection properties at '{pointer}'");
                }
                collectionPointer = pointer;
            }

            var elementType = GetCollectionElementType(propertyType);
            if (collection || (elementType is not null && IsComplex(elementType)))
            {
                if (elementType is null)
                {
                    throw InvalidShape($"collection '{pointer}' has no element type");
                }
                Visit(elementType, pointer + "/*", propertyCulture, depth + 1, ancestry, fields, ref collectionPointer);
                continue;
            }

            if (IsComplex(propertyType))
            {
                Visit(propertyType, pointer, propertyCulture, depth + 1, ancestry, fields, ref collectionPointer);
                continue;
            }

            if (fields.Count >= 200)
            {
                throw InvalidShape($"mapped property limit exceeds 200 at '{pointer}'");
            }
            fields.Add(ToDescriptor(property, pointer, propertyCulture));
        }
        ancestry.Remove(type);
    }

    private FieldDescriptor ToDescriptor(PropertyInfo property, string pointer, string inheritedCulture)
    {
        var field = property.GetCustomAttribute<ScrapeFieldAttribute>();
        var nullability = _nullability.Create(property);
        var propertyType = property.PropertyType;
        var nullable = Nullable.GetUnderlyingType(propertyType) is not null || (!propertyType.IsValueType && nullability.ReadState != NullabilityState.NotNull);
        var requiredMember = property.GetCustomAttribute<RequiredMemberAttribute>() is not null;
        return new FieldDescriptor(pointer, property.Name, propertyType, field?.Required == true || requiredMember || !nullable,
            field?.Description, property.GetCustomAttribute<ScrapeUnitAttribute>()?.Unit,
            property.GetCustomAttribute<ScrapeCultureAttribute>()?.Culture ?? inheritedCulture,
            property.GetCustomAttribute<ScrapeHintAttribute>()?.Hint);
    }

    private static Type? GetCollectionElementType(Type type)
    {
        if (type == typeof(string) || type == typeof(byte[])) return null;
        if (type.IsArray) return type.GetElementType();
        var enumerable = type.GetInterfaces().Append(type).FirstOrDefault(static x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        return enumerable?.GetGenericArguments()[0];
    }

    private static bool IsComplex(Type type) => type.IsClass && type != typeof(string) && type != typeof(Uri) && !typeof(System.Collections.IDictionary).IsAssignableFrom(type);
    private static string EscapePointer(string name) => name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
    private static InvalidOperationException InvalidShape(string detail) => new($"SNR-SCH-001: Schema {detail}.");
    private readonly record struct CacheKey(Type Type, string Culture);
}
