using Sanare.Abstractions.Plans;
using Sanare.Core.Repository;
using Xunit;

namespace Sanare.Core.Tests.Repository;

/// <summary>Covers AC-GIT-012. See docs/features/script-repository.md.</summary>
public sealed class ScriptRepositoryDiffTruncationTests : IDisposable
{
    private readonly ScriptRepositoryFixture _fixture = new();

    [Fact]
    public async Task DiffAsync_caps_patch_text_at_the_plan_size_limit_and_flags_it_truncated()
    {
        var repository = _fixture.Repository;
        await repository.InitializeAsync();

        // Two large plans whose every field differs, so the patch exceeds the 512 KB cap even though each
        // plan document stays under it.
        var first = LargePlan("a");
        var second = LargePlan("b");

        var from = await _fixture.CommitPlanAsync(first, "large first");
        var to = await _fixture.CommitPlanAsync(second, "large second");

        var diff = await repository.DiffAsync(from, to);

        var file = Assert.Single(diff.Files);
        Assert.True(file.Truncated);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(file.Patch) <= ScriptRepositoryOptions.MaxPlanSizeBytes);
    }

    private static ExtractionPlan LargePlan(string marker)
    {
        var template = ScriptRepositoryFixture.SamplePlan();
        var fields = new List<FieldPlan>(330);
        for (var i = 0; i < 330; i++)
        {
            var selector = $".{marker}-{i.ToString(System.Globalization.CultureInfo.InvariantCulture)}-{new string(marker[0], 200)}";
            fields.Add(new FieldPlan(
                $"/Field{i.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                false,
                "string",
                [new LocatorStep(PlanOperation.SelectFirst, [selector])],
                [new TransformStep(PlanOperation.Trim, Array.Empty<string>())]));
        }

        return template with { Fields = fields };
    }

    public void Dispose() => _fixture.Dispose();
}
