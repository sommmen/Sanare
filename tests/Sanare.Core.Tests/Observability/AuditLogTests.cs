using System.Text.Json;
using Sanare.Core.Observability.Audit;

namespace Sanare.Core.Tests.Observability;

public sealed class AuditLogTests : IDisposable
{
    private readonly string _stateRoot = Path.Combine(Path.GetTempPath(), "sanare-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task WriteAsync_Appends_a_single_jsonl_line_to_the_month_file()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero));
        var log = new JsonlAuditLog(_stateRoot, timeProvider);
        var auditEvent = new AuditEvent("PlanApproved", default, "source-a",
            new Dictionary<string, object?> { ["commitId"] = "abc123", ["tag"] = "v1", ["approvedBy"] = "alice" });

        await log.WriteAsync(auditEvent);

        var path = Path.Combine(_stateRoot, "audit", "2026-01.jsonl");
        Assert.True(File.Exists(path));
        var lines = await File.ReadAllLinesAsync(path);
        Assert.Single(lines);
    }

    [Fact]
    public async Task WriteAsync_Serializes_the_PlanApproved_event_with_commit_tag_approvedBy_and_timestamp()
    {
        // AC-027: audit log contains a PlanApproved line with commit, tag, approvedBy, and timestamp.
        var timestamp = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(timestamp);
        var log = new JsonlAuditLog(_stateRoot, timeProvider);
        var auditEvent = new AuditEvent("PlanApproved", default, "source-a",
            new Dictionary<string, object?> { ["commitId"] = "abc123", ["tag"] = "v1", ["approvedBy"] = "alice" });

        await log.WriteAsync(auditEvent);

        var path = Path.Combine(_stateRoot, "audit", "2026-03.jsonl");
        var line = (await File.ReadAllLinesAsync(path)).Single();
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        Assert.Equal("PlanApproved", root.GetProperty("eventType").GetString());
        Assert.Equal("source-a", root.GetProperty("sourceId").GetString());
        Assert.Equal(timestamp, root.GetProperty("timestamp").GetDateTimeOffset());
        var data = root.GetProperty("data");
        Assert.Equal("abc123", data.GetProperty("commitId").GetString());
        Assert.Equal("v1", data.GetProperty("tag").GetString());
        Assert.Equal("alice", data.GetProperty("approvedBy").GetString());
    }

    [Fact]
    public async Task WriteAsync_Never_writes_an_authorization_header_value_to_the_audit_line()
    {
        // AC-026: no audit line contains a redactable secret/PII value.
        var log = new JsonlAuditLog(_stateRoot, new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero)));
        var auditEvent = new AuditEvent("HealDispatched", default, "source-a",
            new Dictionary<string, object?> { ["Authorization"] = "Bearer super-secret-token", ["contact"] = "person@example.com" });

        await log.WriteAsync(auditEvent);

        var path = Path.Combine(_stateRoot, "audit", "2026-05.jsonl");
        var line = (await File.ReadAllLinesAsync(path)).Single();
        Assert.DoesNotContain("Bearer super-secret-token", line, StringComparison.Ordinal);
        Assert.DoesNotContain("person@example.com", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_Rolls_over_to_a_new_month_file_when_the_month_changes()
    {
        var log = new JsonlAuditLog(_stateRoot, new FakeTimeProvider(new DateTimeOffset(2026, 1, 31, 23, 0, 0, TimeSpan.Zero)));
        await log.WriteAsync(new AuditEvent("PlanAuthored", default, "source-a"));

        var laterLog = new JsonlAuditLog(_stateRoot, new FakeTimeProvider(new DateTimeOffset(2026, 2, 1, 1, 0, 0, TimeSpan.Zero)));
        await laterLog.WriteAsync(new AuditEvent("PlanAuthored", default, "source-a"));

        Assert.True(File.Exists(Path.Combine(_stateRoot, "audit", "2026-01.jsonl")));
        Assert.True(File.Exists(Path.Combine(_stateRoot, "audit", "2026-02.jsonl")));
    }

    [Fact]
    public async Task WriteAsync_Throws_and_writes_nothing_when_the_audit_directory_cannot_be_created()
    {
        // AC-OB-002: an audit write failure must fail the operation (durable, failure-fatal writes).
        // Simulate an unwritable audit/ location by pre-creating a file where the directory must go.
        Directory.CreateDirectory(_stateRoot);
        var blockingFilePath = Path.Combine(_stateRoot, "audit");
        await File.WriteAllTextAsync(blockingFilePath, "not a directory");
        var log = new JsonlAuditLog(_stateRoot, new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)));

        await Assert.ThrowsAnyAsync<IOException>(
            async () => await log.WriteAsync(new AuditEvent("PlanApproved", default, "source-a")));
    }

    [Fact]
    public void Constructor_Throws_for_a_null_or_whitespace_state_root()
    {
        Assert.Throws<ArgumentException>(() => new JsonlAuditLog(" "));
    }

    public void Dispose()
    {
        if (Directory.Exists(_stateRoot))
        {
            NormalizeAttributes(_stateRoot);
            Directory.Delete(_stateRoot, recursive: true);
        }
    }

    private static void NormalizeAttributes(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
    }
}
