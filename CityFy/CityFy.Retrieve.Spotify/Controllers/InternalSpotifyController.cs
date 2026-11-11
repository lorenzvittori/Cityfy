using CityFy.Retrieve.Spotify.Services;
using Microsoft.AspNetCore.Mvc;
using ServiceDefault.Errors;

namespace CityFy.Retrieve.Spotify.Controllers
{
    /// <summary>
    /// Service-to-service endpoints used by CityFy.Api (the only service exposed to browsers).
    /// The API authenticates the user and passes the user id; this service is not reachable from outside.
    /// </summary>
    [ApiController]
    [ApiExplorerSettings(GroupName = "Internal")]
    [Route("internal/spotify")]
    public class InternalSpotifyController : ControllerBase
    {
        private readonly ISpotifyAuthService _auth;
        private readonly ISpotifyAccountService _accounts;
        private readonly ISpotifyRetrieveService _retrieve;

        public InternalSpotifyController(ISpotifyAuthService auth, ISpotifyAccountService accounts, ISpotifyRetrieveService retrieve)
        {
            _auth = auth;
            _accounts = accounts;
            _retrieve = retrieve;
        }

        public sealed record LinkRequest(string Code, string? State);

        // GET internal/spotify/authorize-url — Spotify consent URL with a fresh one-time state
        [HttpGet("authorize-url")]
        public IActionResult GetAuthorizeUrl() => Ok(new { url = _auth.CreateAuthorizationUrl() });

        // POST internal/spotify/link — code/state received by the API callback -> linked account
        [HttpPost("link")]
        public async Task<IActionResult> Link([FromBody] LinkRequest request, CancellationToken ct)
        {
            var result = await _auth.CompleteLoginAsync(request.Code, request.State, ct);
            return result.Error switch
            {
                LoginError.InvalidState => this.CityFyProblem(ErrorCodes.SpotifyLoginExpired, "Invalid or expired state", StatusCodes.Status400BadRequest),
                LoginError.TokenExchangeFailed => this.CityFyProblem(ErrorCodes.SpotifyTokenExchangeFailed, "Token exchange with Spotify failed", StatusCodes.Status502BadGateway),
                _ => Ok(new { userId = result.Account!.UserId, displayName = result.Account.DisplayName })
            };
        }

        // GET internal/spotify/accounts/{userId}
        [HttpGet("accounts/{userId}")]
        public async Task<IActionResult> GetAccount(string userId, CancellationToken ct)
        {
            var account = await _accounts.GetAsync(userId, ct) ?? throw SpotifyAccountException.NotLinked(userId);
            return Ok(new
            {
                userId = account.UserId,
                displayName = account.DisplayName,
                linkedAt = account.LinkedAt,
                lastRetrieveAt = account.LastRetrieveAt,
                lastPlayedAt = account.LastPlayedAt
            });
        }

        // POST internal/spotify/accounts/{userId}/retrieve — queue a retrieve now
        [HttpPost("accounts/{userId}/retrieve")]
        public async Task<IActionResult> Retrieve(string userId, CancellationToken ct)
        {
            var jobId = await _retrieve.EnqueueRetrieveAsync(userId, ct);
            return AcceptedAtAction(nameof(GetJob), new { jobId }, new { jobId });
        }

        // GET internal/spotify/jobs/{jobId}
        [HttpGet("jobs/{jobId}")]
        public async Task<IActionResult> GetJob(string jobId, CancellationToken ct)
        {
            var task = await _retrieve.GetJobAsync(jobId, ct);
            if (task == null)
                return this.CityFyProblem(ErrorCodes.RetrieveJobNotFound, $"Retrieve job '{jobId}' not found", StatusCodes.Status404NotFound);

            return Ok(new
            {
                jobId = task.UploadId,
                userId = task.UserId,
                status = task.Status.ToString(),
                inserted = task.Inserted,
                startedAt = task.StartedAt,
                completedAt = task.CompletedAt,
                errorCode = task.ErrorCode
            });
        }
    }
}
