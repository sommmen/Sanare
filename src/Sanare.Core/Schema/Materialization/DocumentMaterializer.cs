using System.Reflection;

namespace Sanare.Core.Schema.Materialization;

/// <summary>Simple reflection materializer for writable v0.1 schema properties.</summary>
public sealed class DocumentMaterializer : IDocumentMaterializer
{
    public TSchema Materialize<TSchema>(IReadOnlyDictionary<string, object?> values) where TSchema : class
    {
        ArgumentNullException.ThrowIfNull(values);
        var instance = Activator.CreateInstance<TSchema>()
            ?? throw new InvalidOperationException($"Cannot create schema type '{typeof(TSchema).Name}'.");

        foreach (var property in typeof(TSchema).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanWrite || !values.TryGetValue("/" + property.Name, out var value) || value is null)
            {
                continue;
            }

            property.SetValue(instance, value);
        }

        return instance;
    }
}
