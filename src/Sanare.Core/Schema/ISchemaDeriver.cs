namespace Sanare.Core.Schema;

/// <summary>Derives deterministic schema metadata from a typed result model.</summary>
public interface ISchemaDeriver
{
    /// <exception cref="ArgumentNullException"><paramref name="schemaType"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The type violates the supported v0.1 schema rules.</exception>
    SchemaDescriptor Derive(Type schemaType, string defaultCulture = "en-US");

    SchemaDescriptor Derive<TSchema>(string defaultCulture = "en-US") where TSchema : class;
}
