using Sanare.Abstractions.Quality;

namespace Sanare.Core.Schema.Quality;

/// <summary>Builds field-level quality observations from materialized schema values.</summary>
public sealed class QualityReportBuilder
{
    public QualityReport Build(
        SchemaDescriptor schema,
        IReadOnlyDictionary<string, object?> values,
        IEnumerable<string>? unmappedFields = null,
        double threshold = 1d)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(values);
        if (threshold is < 0d or > 1d)
        {
            throw new ArgumentOutOfRangeException(nameof(threshold));
        }

        var itemCount = DetermineItemCount(schema, values);
        var fields = schema.Fields.Select(field => BuildFieldHealth(field, values, itemCount)).ToArray();
        var complete = fields.Sum(field => itemCount - (int)Math.Round(field.NullRate * itemCount, MidpointRounding.AwayFromZero));
        var total = fields.Length * itemCount;
        var completeness = total == 0 ? 1d : (double)complete / total;
        return new QualityReport
        {
            Completeness = completeness,
            Fields = fields,
            UnmappedFields = (unmappedFields ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            ItemCount = itemCount,
            MeetsThreshold = completeness >= threshold,
        };
    }

    private static FieldHealth BuildFieldHealth(FieldDescriptor field, IReadOnlyDictionary<string, object?> values, int itemCount)
    {
        var matching = values.Where(pair => MatchesField(pair.Key, field.JsonPointer)).Select(static pair => pair.Value).ToArray();
        var missing = itemCount - matching.Count(static value => value is not null);
        var present = matching.Any(static value => value is not null);
        var nullRate = itemCount == 0 ? 1d : (double)missing / itemCount;
        return new FieldHealth(field.JsonPointer, field.Required, present, present, nullRate);
    }

    private static int DetermineItemCount(SchemaDescriptor schema, IReadOnlyDictionary<string, object?> values)
    {
        if (schema.CollectionPointer is null)
        {
            return 1;
        }

        var prefix = schema.CollectionPointer + "/";
        return values.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
            .Select(key => key[prefix.Length..].Split('/', 2)[0])
            .Where(static segment => int.TryParse(segment, out _))
            .Distinct(StringComparer.Ordinal)
            .Count();
    }

    private static bool MatchesField(string valuePointer, string fieldPointer)
    {
        if (string.Equals(valuePointer, fieldPointer, StringComparison.Ordinal))
        {
            return true;
        }

        const string marker = "/*";
        var markerIndex = fieldPointer.IndexOf(marker, StringComparison.Ordinal);
        return markerIndex >= 0 && valuePointer.StartsWith(fieldPointer[..markerIndex] + "/", StringComparison.Ordinal) &&
            valuePointer.EndsWith(fieldPointer[(markerIndex + marker.Length)..], StringComparison.Ordinal);
    }
}
