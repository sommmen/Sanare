namespace Sanare.Core.Schema.Materialization;

/// <summary>Materialises validated field values as a typed schema object.</summary>
public interface IDocumentMaterializer
{
    TSchema Materialize<TSchema>(IReadOnlyDictionary<string, object?> values) where TSchema : class;
}
