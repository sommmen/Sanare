using Sanare.Abstractions;
using Sanare.Core.Fixtures;
using Sanare.Core.Fixtures.Retention;
using Xunit;

namespace Sanare.Core.Tests.Fixtures;

/// <summary>DR-011 pyramid retention arithmetic: the `Full` apex cap, pinned vs. unpinned `Slice`
/// fixtures, referenced-tag, and protected-role cases (docs/features/fixture-corpus.md AC-013,
/// AC-FIX-008, AC-FIX-009, AC-FIX-017, AC-FIX-018).</summary>
public sealed class FixturePrunerTests
{
    private readonly FixturePruner _pruner = new();

    [Fact]
    public void SelectForRemoval_keeps_only_the_newest_FullRetentionCount_unreferenced_Full_fixtures()
    {
        // AC-FIX-008: 25 unreferenced Full fixtures, FullRetentionCount 3 => exactly the 3 newest survive.
        var records = Enumerable.Range(0, 25).Select(index => Full("source-a", index)).ToArray();

        var removed = _pruner.SelectForRemoval(records, fullRetentionCount: 3, protectedRoles: [], protectedFixtureIds: []);

        Assert.Equal(22, removed.Count);
        var survivors = records.Except(removed).Select(record => record.Id).ToHashSet();
        var expectedSurvivors = records.OrderByDescending(record => record.CapturedAt).Take(3).Select(record => record.Id);
        Assert.Equal(expectedSurvivors.ToHashSet(), survivors);
    }

    [Fact]
    public void SelectForRemoval_never_removes_fixtures_referenced_by_an_approved_tag()
    {
        // AC-013: 25 Full fixtures, 12 tag-referenced, FullRetentionCount 3 => 12 referenced + 3 newest
        // unreferenced Full survive (i.e. exactly 25 - 12 - 3 = 10 are removed).
        var referenced = Enumerable.Range(0, 12).Select(index => Full("source-a", index, tags: ["approved/source-a/Schema@1/1"])).ToArray();
        var unreferenced = Enumerable.Range(12, 13).Select(index => Full("source-a", index)).ToArray();
        var records = referenced.Concat(unreferenced).ToArray();

        var removed = _pruner.SelectForRemoval(records, fullRetentionCount: 3, protectedRoles: [], protectedFixtureIds: []);

        Assert.Equal(10, removed.Count);
        Assert.DoesNotContain(removed, record => referenced.Contains(record));
        var expectedSurvivingUnreferenced = unreferenced.OrderByDescending(record => record.CapturedAt).Take(3);
        Assert.All(expectedSurvivingUnreferenced, record => Assert.DoesNotContain(record, removed));
    }

    [Fact]
    public void SelectForRemoval_keeps_a_protected_page_role_fixture_regardless_of_age()
    {
        // AC-FIX-009: a fixture with PageRole "consent-wall" survives even with 30 newer fixtures.
        var protectedFixture = Full("source-a", 0, pageRole: "consent-wall");
        var newer = Enumerable.Range(1, 30).Select(index => Full("source-a", index)).ToArray();
        var records = new[] { protectedFixture }.Concat(newer).ToArray();

        var removed = _pruner.SelectForRemoval(records, fullRetentionCount: 3, protectedRoles: ["consent-wall", "empty-result", "not-found"], protectedFixtureIds: []);

        Assert.DoesNotContain(protectedFixture, removed);
    }

    [Fact]
    public void SelectForRemoval_never_removes_a_pinned_Slice_fixture_even_with_50_newer_Full_captures()
    {
        // AC-FIX-017: a Slice fixture with a non-null PinnedIssue survives indefinitely — the pyramid
        // base is not subject to the apex cap.
        var pinnedSlice = Slice("source-a", 0, pinnedIssue: "BUG-123");
        var newerFull = Enumerable.Range(1, 50).Select(index => Full("source-a", index)).ToArray();
        var records = new[] { pinnedSlice }.Concat(newerFull).ToArray();

        var removed = _pruner.SelectForRemoval(records, fullRetentionCount: 3, protectedRoles: [], protectedFixtureIds: []);

        Assert.DoesNotContain(pinnedSlice, removed);
    }

    [Fact]
    public void SelectForRemoval_removes_an_unpinned_untagged_unprotected_Slice_fixture()
    {
        // AC-FIX-018: an unpinned Slice fixture with no protecting tag/role is prunable like any other
        // unprotected fixture — it is scratch output, not a retained bug reproduction.
        var unpinnedSlice = Slice("source-a", 0, pinnedIssue: null);

        var removed = _pruner.SelectForRemoval([unpinnedSlice], fullRetentionCount: 3, protectedRoles: [], protectedFixtureIds: []);

        Assert.Contains(unpinnedSlice, removed);
    }

    [Fact]
    public void SelectForRemoval_treats_each_sourceId_independently()
    {
        var sourceA = Enumerable.Range(0, 5).Select(index => Full("source-a", index)).ToArray();
        var sourceB = Enumerable.Range(0, 5).Select(index => Full("source-b", index)).ToArray();

        var removed = _pruner.SelectForRemoval(sourceA.Concat(sourceB).ToArray(), fullRetentionCount: 3, protectedRoles: [], protectedFixtureIds: []);

        Assert.Equal(4, removed.Count);
        Assert.Equal(2, removed.Count(record => record.SourceId == "source-a"));
        Assert.Equal(2, removed.Count(record => record.SourceId == "source-b"));
    }

    private static FixtureRecord Full(string sourceId, int ageIndex, string? pageRole = null, IReadOnlyList<string>? tags = null) =>
        new($"{sourceId}/full-{ageIndex}", sourceId, $"https://example.test/{sourceId}/{ageIndex}",
            DateTimeOffset.UnixEpoch + TimeSpan.FromMinutes(ageIndex), AcquisitionTier.Html, "text/html",
            $"{sourceId}/full-{ageIndex}.html", 1024, "sha256:" + ageIndex, "sha256:norm-" + ageIndex,
            [], tags ?? [], pageRole, null, RetentionTier.Full, null);

    private static FixtureRecord Slice(string sourceId, int ageIndex, string? pinnedIssue) =>
        new($"{sourceId}/slice-{ageIndex}", sourceId, $"https://example.test/{sourceId}/slice-{ageIndex}",
            DateTimeOffset.UnixEpoch + TimeSpan.FromMinutes(ageIndex), AcquisitionTier.Html, "text/html",
            $"{sourceId}/slice-{ageIndex}.html", 256, "sha256:s" + ageIndex, "sha256:norm-s" + ageIndex,
            [], [], null, null, RetentionTier.Slice, pinnedIssue);
}
