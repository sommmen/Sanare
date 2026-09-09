using System.Text;
using System.Text.Json;
using Sanare.Core.Observability.Redaction;

namespace Sanare.Core.Observability.Audit;

/// <summary>Durably appends redacted audit events to the state-root monthly JSONL file.</summary>
public sealed class JsonlAuditLog : IAuditLog
{
    private readonly string _stateRoot;
    private readonly TimeProvider _timeProvider;
    private readonly RedactionPolicy _redactionPolicy;

    public JsonlAuditLog(string stateRoot, TimeProvider? timeProvider = null, RedactionPolicy? redactionPolicy = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateRoot);
        _stateRoot = stateRoot;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _redactionPolicy = redactionPolicy ?? new RedactionPolicy();
    }

    public async ValueTask WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        var timestamp = auditEvent.Timestamp == default ? _timeProvider.GetUtcNow() : auditEvent.Timestamp;
        var sanitizedData = auditEvent.Data?.ToDictionary(pair => pair.Key, pair => _redactionPolicy.RedactValue(pair.Key, pair.Value));
        var sanitized = auditEvent with { Timestamp = timestamp, Data = sanitizedData };
        var month = timestamp.UtcDateTime.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        var path = Path.Combine(_stateRoot, "audit", $"{month}.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var payload = JsonSerializer.Serialize(sanitized, AuditEventJsonContext.Default.AuditEvent) + Environment.NewLine;

        await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough | FileOptions.Asynchronous);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 4096, leaveOpen: true);
        await writer.WriteAsync(payload.AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }
}
