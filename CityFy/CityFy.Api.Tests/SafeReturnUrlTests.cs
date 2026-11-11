using CityFy.Api.Auth;

namespace CityFy.Api.Tests;

public class SafeReturnUrlTests
{
    [Theory]
    [InlineData("/city", "/city")]
    [InlineData("/city?period=30d", "/city?period=30d")]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("https://evil.example/phish", "/")]   // absolute URL
    [InlineData("//evil.example", "/")]              // protocol-relative
    [InlineData("/\\evil.example", "/")]             // backslash trick
    [InlineData("city", "/")]                        // not rooted
    public void OnlyLocalPathsAreAllowed(string? input, string expected)
    {
        Assert.Equal(expected, AuthSetup.SafeReturnUrl(input));
    }
}
