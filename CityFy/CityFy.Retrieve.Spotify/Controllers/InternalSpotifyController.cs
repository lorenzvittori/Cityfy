using CityFy.Retrieve.Spotify.Models;
using CityFy.Retrieve.Spotify.Services;
using Microsoft.AspNetCore.Mvc;
using ServiceDefault.Errors;
using ServiceDefault.Models;

namespace CityFy.Retrieve.Spotify.Controllers;

/// <summary>
/// Service-to-service endpoints used by CityFy.Api (the only service exposed to browsers).
/// The API authenticates the user and passes the user id; this service is not reachable from outside.
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = "Internal")]
[Route("internal/spotify")]
public sealed class InternalSpotifyController(
    ISpotifyAuthService auth,
    ISpotifyAccountService accounts,
    ISpotifyRetrieveService retrieve,
    IUserDataService userData) : ControllerBase
{
    // GET internal/spotify/authorize-url — Spotify consent URL with a fresh one-time state
    [HttpGet("authorize-url")]
    public ActionResult<AuthorizeUrlResponse> GetAuthorizeUrl() => Ok(new AuthorizeUrlResponse(auth.CreateAuthorizationUrl()));

    // POST internal/spotify/link — code/state received by the API callback -> linked account
    [HttpPost("link")]
    public async Task<ActionResult<LinkResponse>> Link([FromBody] LinkRequest request, CancellationToken ct)
    {
        var result = await auth.CompleteLoginAsync(request.Code, request.State, ct);
        return result.Error switch
        {
            LoginError.InvalidState => this.CityFyProblem(ErrorCodes.SpotifyLoginExpired, "Invalid or expired state", StatusCodes.Status400BadRequest),
            LoginError.TokenExchangeFailed => this.CityFyProblem(ErrorCodes.SpotifyTokenExchangeFailed, "Token exchange with Spotify failed", StatusCodes.Status502BadGateway),
            _ => Ok(new LinkResponse(result.Account!.UserId, result.Account.DisplayName))
        };
    }

    // GET internal/spotify/accounts — every linked account (admin panel), without tokens
    [HttpGet("accounts")]
    public async Task<ActionResult<IReadOnlyList<SpotifyAccountResponse>>> ListAccounts(CancellationToken ct) =>
        Ok((await accounts.ListAsync(ct)).Select(SpotifyAccountResponse.From).ToList());

    // GET internal/spotify/accounts/{userId}
    [HttpGet("accounts/{userId}")]
    public async Task<ActionResult<SpotifyAccountResponse>> GetAccount(string userId, CancellationToken ct)
    {
        var account = await accounts.GetAsync(userId, ct) ?? throw SpotifyAccountException.NotLinked(userId);
        return Ok(SpotifyAccountResponse.From(account));
    }

    // POST internal/spotify/accounts/{userId}/retrieve — queue a retrieve now
    [HttpPost("accounts/{userId}/retrieve")]
    public async Task<ActionResult<JobAcceptedResponse>> Retrieve(string userId, CancellationToken ct)
    {
        var jobId = await retrieve.EnqueueRetrieveAsync(userId, ct);
        return AcceptedAtAction(nameof(GetJob), new { jobId }, new JobAcceptedResponse(jobId));
    }

    // GET internal/spotify/jobs?limit=30 — most recent retrieve jobs of every user (admin panel)
    [HttpGet("jobs")]
    public async Task<ActionResult<IReadOnlyList<RetrieveJobResponse>>> ListJobs([FromQuery] int limit = 30, CancellationToken ct = default) =>
        Ok((await retrieve.ListJobsAsync(Math.Clamp(limit, 1, 200), ct)).Select(RetrieveJobResponse.From).ToList());

    // GET internal/spotify/jobs/{jobId}
    [HttpGet("jobs/{jobId}")]
    public async Task<ActionResult<RetrieveJobResponse>> GetJob(string jobId, CancellationToken ct)
    {
        var task = await retrieve.GetJobAsync(jobId, ct);
        return task == null
            ? this.CityFyProblem(ErrorCodes.RetrieveJobNotFound, $"Retrieve job '{jobId}' not found", StatusCodes.Status404NotFound)
            : Ok(RetrieveJobResponse.From(task));
    }

    // POST internal/spotify/jobs/{jobId}/cancel — admin
    [HttpPost("jobs/{jobId}/cancel")]
    public async Task<ActionResult<JobCancelledResponse>> CancelJob(string jobId, CancellationToken ct)
    {
        await retrieve.CancelAsync(jobId, ct);
        return Accepted(new JobCancelledResponse(jobId, nameof(ProcessingStatus.Cancelled)));
    }

    // DELETE internal/spotify/users/{userId}/data — stop the user's retrieves, delete jobs and account
    [HttpDelete("users/{userId}/data")]
    public async Task<ActionResult<UserDataDeletion>> DeleteUserData(string userId, CancellationToken ct) =>
        Ok(await userData.DeleteAsync(userId, ct));
}
