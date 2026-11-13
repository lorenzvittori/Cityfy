using CityFy.Api.Auth;
using CityFy.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServiceDefault.Errors;

namespace CityFy.Api.Controllers;

/// <summary>
/// Live events for the open pages (Server-Sent Events): what the user is listening to. Only for
/// accounts (the track comes from Last.fm); the browser reconnects by itself (EventSource).
/// </summary>
[ApiController]
[Authorize]
[ApiExplorerSettings(GroupName = "v1")]
[Route("api/v1/live")]
public sealed class LiveController(ILiveService live) : ControllerBase
{
    // GET api/v1/live — text/event-stream: "now-playing" (NowPlayingDto) and "ping"
    [HttpGet]
    public async Task<IResult> Get(CancellationToken ct)
    {
        if (User.LastFmUsername() is null)
            throw new CityFyException(ErrorCodes.LastFmNotLinked, "Live events need a Last.fm account", StatusCodes.Status404NotFound);

        var userId = User.UserId();
        var connection = await live.OpenAsync(userId, ct);
        return TypedResults.ServerSentEvents(live.ReadAsync(userId, connection, HttpContext.RequestAborted));
    }
}
