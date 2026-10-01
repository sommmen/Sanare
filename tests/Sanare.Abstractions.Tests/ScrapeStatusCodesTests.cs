using System.Text.RegularExpressions;

namespace Sanare.Abstractions.Tests;

public sealed partial class ScrapeStatusCodesTests
{
    [GeneratedRegex("^SNR-[A-Z]{3,5}-\\d{3}$")]
    private static partial Regex CodePattern();

    public static TheoryData<ScrapeStatus> AllStatuses()
    {
        var data = new TheoryData<ScrapeStatus>();
        foreach (var status in Enum.GetValues<ScrapeStatus>())
        {
            data.Add(status);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllStatuses))]
    public void For_returns_distinct_codes_matching_the_SNR_pattern(ScrapeStatus status)
    {
        var codes = ScrapeStatusCodes.For(status);

        Assert.Equal(codes.Count, codes.Distinct().Count());
        Assert.All(codes, code => Assert.Matches(CodePattern(), code));
    }

    [Fact]
    public void For_throws_for_an_undefined_status_value()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ScrapeStatusCodes.For((ScrapeStatus)(-1)));
    }

    [Fact]
    public void For_BrowserFailed_maps_to_all_five_SNR_BRW_codes()
    {
        // Pins the full SNR-BRW-* error table (docs/sanare/tech-design.md §"Error catalogue" and
        // docs/features/browser-tier.md) so a future edit cannot silently drop a code from the switch
        // body without a test noticing — the shape-only theory above would not catch that.
        var codes = ScrapeStatusCodes.For(ScrapeStatus.BrowserFailed);

        Assert.Equal(
            ["SNR-BRW-001", "SNR-BRW-002", "SNR-BRW-003", "SNR-BRW-004", "SNR-BRW-005"],
            codes);
    }
}
