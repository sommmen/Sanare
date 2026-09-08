using Sanare.Core.Resolution;
using Xunit;

namespace Sanare.Core.Tests.Resolution;

public sealed class ApprovalTagParserTests
{
    [Fact]
    public void BuildPrefix_matches_convention()
    {
        var prefix = ApprovalTagParser.BuildPrefix("lenovo/tablets", "Product", 1);

        Assert.Equal("approved/lenovo/tablets/Product@1/", prefix);
    }

    [Theory]
    [InlineData("approved/lenovo/tablets/Product@1/9", true, 9)]
    [InlineData("approved/lenovo/tablets/Product@1/10", true, 10)]
    [InlineData("approved/lenovo/tablets/Product@1/", false, 0)]
    [InlineData("approved/lenovo/tablets/Product@1/abc", false, 0)]
    [InlineData("approved/other/tablets/Product@1/9", false, 0)]
    public void TryParseNumber_parses_or_rejects(string tagName, bool expectedSuccess, int expectedNumber)
    {
        var prefix = ApprovalTagParser.BuildPrefix("lenovo/tablets", "Product", 1);

        var success = ApprovalTagParser.TryParseNumber(tagName, prefix, out var number);

        Assert.Equal(expectedSuccess, success);
        Assert.Equal(expectedNumber, number);
    }

    [Fact]
    public void Ordering_by_parsed_number_ranks_10_above_9()
    {
        var prefix = ApprovalTagParser.BuildPrefix("lenovo/tablets", "Product", 1);
        var tags = new[] { prefix + "9", prefix + "10", prefix + "2" };

        var ordered = tags
            .Select(t => (Tag: t, Number: ApprovalTagParser.TryParseNumber(t, prefix, out var n) ? n : -1))
            .OrderByDescending(t => t.Number)
            .Select(t => t.Tag)
            .ToArray();

        Assert.Equal([prefix + "10", prefix + "9", prefix + "2"], ordered);
    }
}
