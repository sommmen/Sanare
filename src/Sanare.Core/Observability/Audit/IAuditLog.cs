namespace Sanare.Core.Observability.Audit;

/// <summary>Durable append-only audit storage for security-relevant operations.</summary>
public interface IAuditLog
{
    ValueTask WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);
}
