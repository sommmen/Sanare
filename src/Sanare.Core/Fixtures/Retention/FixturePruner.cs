namespace Sanare.Core.Fixtures.Retention;

public sealed class FixturePruner
{
    /// <summary>
    /// Selects prunable fixtures under the DR-011 pyramid: fixtures are protected (never returned) when
    /// explicitly named in <paramref name="protectedFixtureIds"/>, when their
    /// <see cref="FixtureRecord.ReferencedByTags"/> is non-empty (populated only with approved tags by the
    /// caller), when their <see cref="FixtureRecord.PageRole"/> is in <paramref name="protectedRoles"/>, or
    /// when they are a <see cref="RetentionTier.Slice"/> fixture with a non-null
    /// <see cref="FixtureRecord.PinnedIssue"/>. Remaining <see cref="RetentionTier.Full"/> fixtures are kept
    /// up to <paramref name="fullRetentionCount"/> newest per source; everything else prunable is returned.
    /// </summary>
    public IReadOnlyList<FixtureRecord> SelectForRemoval(
        IReadOnlyCollection<FixtureRecord> records,
        int fullRetentionCount,
        IReadOnlyCollection<string> protectedRoles,
        IReadOnlyCollection<string> protectedFixtureIds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fullRetentionCount);
        var roleSet = new HashSet<string>(protectedRoles, StringComparer.OrdinalIgnoreCase);
        var idSet = new HashSet<string>(protectedFixtureIds, StringComparer.Ordinal);
        var remove = new List<FixtureRecord>();

        foreach (var source in records.GroupBy(record => record.SourceId, StringComparer.Ordinal))
        {
            var protectedRecords = source.Where(record =>
                idSet.Contains(record.Id) ||
                record.ReferencedByTags.Count > 0 ||
                (record.PageRole is not null && roleSet.Contains(record.PageRole)) ||
                (record.RetentionTier == RetentionTier.Slice && record.PinnedIssue is not null)).ToHashSet();
            var fullToKeep = source.Where(record => record.RetentionTier == RetentionTier.Full && !protectedRecords.Contains(record))
                .OrderByDescending(record => record.CapturedAt)
                .Take(fullRetentionCount)
                .ToHashSet();

            remove.AddRange(source.Where(record => !protectedRecords.Contains(record) &&
                (record.RetentionTier == RetentionTier.Slice || !fullToKeep.Contains(record))));
        }
        return remove;
    }
}
