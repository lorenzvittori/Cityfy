using CityFy.Elaboration.Services;

namespace CityFy.Elaboration.Tests;

public class PeriodsTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(null, "all")]
    [InlineData("ALL", "all")]
    [InlineData(" 30d ", "30d")]
    [InlineData("6M", "6m")]
    [InlineData("1y", "1y")]
    public void TryNormalize_AcceptsValidPeriods(string? input, string expected)
    {
        Assert.True(Periods.TryNormalize(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("0d")]
    [InlineData("30")]
    [InlineData("1w")]
    [InlineData("-1y")]
    public void TryNormalize_RejectsInvalidPeriods(string input)
    {
        Assert.False(Periods.TryNormalize(input, out _));
    }

    [Fact]
    public void GetStart_ComputesTheStartOfThePeriod()
    {
        Assert.Null(Periods.GetStart("all", Now));
        Assert.Equal(Now.AddDays(-30), Periods.GetStart("30d", Now));
        Assert.Equal(Now.AddMonths(-6), Periods.GetStart("6m", Now));
        Assert.Equal(Now.AddYears(-1), Periods.GetStart("1y", Now));
    }
}
