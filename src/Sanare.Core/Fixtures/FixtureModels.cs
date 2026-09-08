using Sanare.Abstractions;
using Sanare.Abstractions.Diagnostics;

namespace Sanare.Core.Fixtures;

public enum RetentionTier { Full, Slice }

public sealed record FixtureRecord(
    string Id, string SourceId, string Url, DateTimeOffset CapturedAt,
    AcquisitionTier Tier, string ContentType, string File, long Bytes,
    string ContentHash, string NormalisedHash,
    IReadOnlyList<string> Redactions, IReadOnlyList<string> ReferencedByTags,
    string? PageRole, string? Notes,
    RetentionTier RetentionTier, string? PinnedIssue,
    DateTimeOffset? LastSeenAt = null, bool IsTruncated = false);

public sealed record FixtureContent(byte[] Bytes, string ContentType, FixtureRecord Record);

public sealed record FixtureQuery(
    string? FixtureId = null,
    string? SourceId = null,
    string? Url = null,
    string? PageRole = null,
    bool Latest = false);

public sealed record CaptureRequest(
    string Url,
    string SourceId,
    AcquisitionTier Tier,
    string ContentType,
    Stream Body,
    IReadOnlyDictionary<string, string>? ResponseHeaders = null,
    string? PageRole = null,
    string? Notes = null,
    RetentionTier RetentionTier = RetentionTier.Full,
    string? PinnedIssue = null,
    IReadOnlyList<string>? ReferencedByTags = null);

public sealed record FixtureSlice(
    string FixtureId, string Content, int ElidedCharacterCount,
    IReadOnlyList<ScrapeDiagnostic> Diagnostics);

public sealed record FixtureSliceRequest(
    string? Selector = null, long? Offset = null, int ContextCharacters = 4000);

public sealed record PruneReport(IReadOnlyList<FixtureRecord> Removed)
{
    public int RemovedCount => Removed.Count;
}

public sealed record FixtureOptions(
    string StateRoot,
    int FullRetentionCount = 3,
    bool Offline = false,
    IReadOnlyCollection<string>? ProtectedPageRoles = null,
    IReadOnlyCollection<string>? VolatileJsonKeys = null)
{
    public const int MaximumFixtureBytes = 8 * 1024 * 1024;
    public IReadOnlyCollection<string> EffectiveProtectedPageRoles =>
        ProtectedPageRoles ?? ["consent-wall", "empty-result", "not-found"];
    public IReadOnlyCollection<string> EffectiveVolatileJsonKeys =>
        VolatileJsonKeys ?? ["requestId", "timestamp", "sessionId", "nonce"];
}

public sealed record FixtureManifest(int Version, IReadOnlyList<FixtureRecord> Fixtures);

public sealed class FixtureCorpusException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
