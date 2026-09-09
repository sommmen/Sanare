namespace Sanare.Core.Observability.Redaction;

/// <summary>Identifies source/schema fields whose PII values may be emitted to a log.</summary>
public sealed class PiiAllowList
{
    private readonly HashSet<(string SourceId, string Field)> _entries = new();

    public PiiAllowList(IEnumerable<(string SourceId, string Field)>? entries = null)
    {
        if (entries is not null)
        {
            foreach (var entry in entries)
            {
                _entries.Add(entry);
            }
        }
    }

    public bool Allows(string sourceId, string field) => _entries.Contains((sourceId, field));
}
