using System.Net;
using CityFy.Retrieve.Spotify.Services;
using ServiceDefault.Errors;

namespace CityFy.Retrieve.Spotify.Tests;

/// <summary>Which code the UI receives for a failed retrieve job (see ERRORI_UI.md §4).</summary>
public sealed class ErrorCodeMappingTests
{
    public static TheoryData<Exception, string> Cases => new()
    {
        { SpotifyAccountException.NotLinked("u1"), ErrorCodes.SpotifyAccountNotLinked },
        { SpotifyAccountException.ReauthRequired("revoked"), ErrorCodes.SpotifyReauthRequired },
        { new HttpRequestException("429", null, HttpStatusCode.TooManyRequests), ErrorCodes.SpotifyRateLimited },
        { new HttpRequestException("401", null, HttpStatusCode.Unauthorized), ErrorCodes.SpotifyReauthRequired },
        { new HttpRequestException("503", null, HttpStatusCode.ServiceUnavailable), ErrorCodes.SpotifyUnavailable },
        { new HttpRequestException("network down"), ErrorCodes.SpotifyUnavailable },
        { new TaskCanceledException("timeout"), ErrorCodes.SpotifyUnavailable },
        { new HttpRequestException("400", null, HttpStatusCode.BadRequest), ErrorCodes.SpotifyRetrieveFailed },
        { new InvalidOperationException("bug"), ErrorCodes.SpotifyRetrieveFailed },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void FailedRetrieve_IsMappedToAUiCode(Exception error, string expected)
    {
        Assert.Equal(expected, SpotifyRetrieveService.ToErrorCode(error));
    }

    [Fact]
    public void AccountErrors_CarryTheHttpStatusForTheApi()
    {
        Assert.Equal(404, SpotifyAccountException.NotLinked("u1").StatusCode);
        Assert.Equal(401, SpotifyAccountException.ReauthRequired("x").StatusCode);
    }
}
