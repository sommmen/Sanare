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
}
