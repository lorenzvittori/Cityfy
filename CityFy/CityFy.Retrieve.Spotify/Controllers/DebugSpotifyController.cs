using CityFy.Retrieve.Spotify.Services;
using Microsoft.AspNetCore.Mvc;
using ServiceDefault.Errors;

namespace CityFy.Retrieve.Spotify.Controllers
{
    [ApiController]
    [Route("debug/spotify")]
    public class DebugSpotifyController : ControllerBase
    {
        private readonly ISpotifyRetrieveService _service;
        private readonly ISpotifyAuthService _authService;

        public DebugSpotifyController(ISpotifyRetrieveService service, ISpotifyAuthService authService)
        {
            _service = service;
            _authService = authService;
        }

        // GET debug/spotify/login
        // Starts the Authorization Code flow. The path stays under debug/ because the redirect URI
        // registered on the Spotify dashboard points to debug/spotify/callback; F3 will expose
        // login through the API gateway.
        [HttpGet("login")]
        public IActionResult Login()
        {
            var url = _authService.CreateAuthorizationUrl();

            // Called via fetch/AJAX: return the URL so the client performs a top-level navigation
            var accept = Request.Headers.Accept.ToString();
            var xRequestedWith = Request.Headers["X-Requested-With"].ToString();
            if (!string.IsNullOrWhiteSpace(xRequestedWith) || accept.Contains("application/json", StringComparison.OrdinalIgnoreCase))
                return Ok(new { url });

            return Redirect(url);
        }

        // GET debug/spotify/callback?code=...&state=...
        // Validates state, exchanges the code and links the account. Tokens are never returned:
        // the refresh token is stored encrypted, the access token stays in memory.
        [HttpGet("callback")]
        public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error, CancellationToken ct)
        {
            // "access_denied" when the user refuses on the Spotify consent page
            if (!string.IsNullOrWhiteSpace(error))
                return this.CityFyProblem(ErrorCodes.SpotifyAuthDenied, $"Spotify authorization denied: {error}", StatusCodes.Status400BadRequest);
            if (string.IsNullOrWhiteSpace(code))
                return this.CityFyProblem(ErrorCodes.ValidationFailed, "Missing code", StatusCodes.Status400BadRequest);

            var result = await _authService.CompleteLoginAsync(code, state, ct);
            return result.Error switch
            {
                LoginError.InvalidState => this.CityFyProblem(ErrorCodes.SpotifyLoginExpired,
                    "Invalid or expired state: start again from /debug/spotify/login", StatusCodes.Status400BadRequest),
                LoginError.TokenExchangeFailed => this.CityFyProblem(ErrorCodes.SpotifyTokenExchangeFailed,
                    "Token exchange with Spotify failed", StatusCodes.Status502BadGateway),
                _ => Ok(new { userId = result.Account!.UserId, displayName = result.Account.DisplayName, linked = true })
            };
        }

        // POST debug/spotify/start/{userId}
        // Queues a retrieve for a linked account; status via GET debug/spotify/jobs/{jobId}.
        // Not linked -> 404 SPOTIFY_ACCOUNT_NOT_LINKED (SpotifyAccountException, shared error handler).
        [DevelopmentOnly]
        [HttpPost("start/{userId}")]
        public async Task<IActionResult> StartRetrieve(string userId, CancellationToken ct)
        {
            var jobId = await _service.EnqueueRetrieveAsync(userId, ct);
            return AcceptedAtAction(nameof(GetJob), new { jobId }, new { jobId });
        }

        // GET debug/spotify/jobs/{jobId}
        [DevelopmentOnly]
        [HttpGet("jobs/{jobId}")]
        public async Task<IActionResult> GetJob(string jobId, CancellationToken ct)
        {
            var task = await _service.GetJobAsync(jobId, ct);
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
                errorCode = task.ErrorCode,
                message = task.Message
            });
        }
    }
}
