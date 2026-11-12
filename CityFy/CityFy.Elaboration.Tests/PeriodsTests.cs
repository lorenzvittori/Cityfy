using CityFy.Elaboration.SubServices;

namespace CityFy.Elaboration.Tests;

public sealed class PeriodsTests
{
    private static readonly PeriodSubService Period = new();

    private static readonly DateTime Now = new(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(null, "all")]
    [InlineData("ALL", "all")]
    [InlineData(" 30d ", "30d")]
    [InlineData("6M", "6m")]
    [InlineData("1y", "1y")]
    public void TryNormalize_AcceptsValidPeriods(string? input, string expected)
    {
        Assert.True(Period.TryNormalize(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("0d")]
    [InlineData("30")]
    [InlineData("1w")]
    [InlineData("-1y")]
    public void TryNormalize_RejectsInvalidPeriods(string input)
    {
        Assert.False(Period.TryNormalize(input, out _));
    }

    [Fact]
    public void GetStart_ComputesTheStartOfThePeriod()
    {
        Assert.Null(Period.GetStart("all", Now));
        Assert.Equal(Now.AddDays(-30), Period.GetStart("30d", Now));
        Assert.Equal(Now.AddMonths(-6), Period.GetStart("6m", Now));
        Assert.Equal(Now.AddYears(-1), Period.GetStart("1y", Now));
    }
}
