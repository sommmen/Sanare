using System.Text.Json.Serialization;

namespace Sanare.Core.Observability.Audit;

[JsonSerializable(typeof(AuditEvent))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public sealed partial class AuditEventJsonContext : JsonSerializerContext;
