using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CityFy.RtrieveSpotify.Services;

namespace CityFy.RtrieveSpotify.Controllers
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

        // POST debug/spotify/start
        // Avvia un task asincrono di retrieve e ritorna 202 Accepted immediatamente.
        [HttpPost("start")]
        public IActionResult StartRetrieve()
        {
            if (!Request.Headers.TryGetValue("Authorization", out var auth) || string.IsNullOrWhiteSpace(auth))
                return Unauthorized("Missing Authorization header. Provide 'Authorization: Bearer {token}'");

            var token = auth.ToString();
            try
            {
                // Solo orchestration: avvia il servizio in background
                Task.Run(() => _service.StartRetrieveAsync(token));
                return Accepted();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Debug Spotify retrieve failed: " + ex);
                return StatusCode(500);
            }
        }

        // GET debug/spotify/token
        // Endpoint di utilità per debug: ritorna il token passato nell'header Authorization.
        [HttpGet("token")]
        public IActionResult GetToken()
        {
            if (!Request.Headers.TryGetValue("Authorization", out var auth) || string.IsNullOrWhiteSpace(auth))
                return Unauthorized("Missing Authorization header. Provide 'Authorization: Bearer {token}'");

            var token = auth.ToString();
            return Ok(new { token });
        }

        // GET debug/spotify/login
        // Redirects the user to Spotify authorization page (Authorization Code flow)
        [HttpGet("login")]
        public IActionResult Login()
        {
            var url = _authService.GetAuthorizationUrl();

            // If the endpoint is invoked via AJAX/fetch (Accept: application/json or X-Requested-With header)
            // return the authorization URL as JSON so the client can perform a top-level navigation.
            var accept = Request.Headers["Accept"].ToString();
            var xRequestedWith = Request.Headers["X-Requested-With"].ToString();
            if (!string.IsNullOrWhiteSpace(xRequestedWith) || (!string.IsNullOrWhiteSpace(accept) && accept.Contains("application/json", StringComparison.OrdinalIgnoreCase)))
            {
                return Ok(new { url });
            }

            // Otherwise perform a server-side redirect (normal browser navigation)
            return Redirect(url);
        }

        // GET debug/spotify/callback?code=...&state=...
        // Exchanges authorization code for access token and returns it
        /// <summary>
        /// {
        ///     "access_token":"BQCvz67uGo7hvcfwtS6HaaKAkoa9l3hi829ka66gZihk6MH7laAGGDCRkNYJt6zXuf1FY5BeWURoNrtbNjyoqknZMmls1u8b6J0iI6O9K025MsMYgneDe-KPa6GnOIpPfivQ3rQOFo94Sf6D_r_Hqrsgiz_7xoOyBpnRPkR0U7_TFJ1KEnRKJT9hV2zD-N6WaHp0br6rplos-IhtFRCCcrANe5M3XpmrtoO1pDCtM3iXF8KwX55QGLiN3t-TGXSctaKAI_HsUGI",
        ///     "refresh_token":"AQBamLwmmr9p9Zbjj6NZuECPsJdMlspljMQO7LpwLjcoPRNV2glVHWzpz_tyZwaOc6czKSlzDppnbfCJnZIX-qFeYk6gViQOQ6fHVDoqHc91bcchLpuHgiB50yWQA7sIUew",
        ///     "expires_in":3600,
        ///     "scope":"user-modify-playback-state user-library-read user-read-playback-state user-read-private user-top-read"}
        /// }
        /// </summary>
        /// <param name="code"></param>
        /// <param name="state"></param>
        /// <returns></returns>

        [HttpGet("callback")]
        public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state)
        {
            if (string.IsNullOrWhiteSpace(code))
                return BadRequest("Missing code");

            var token = await _authService.ExchangeCodeAsync(code);
            if (token == null)
                return StatusCode(500, "Token exchange failed");

            return Ok(new { access_token = token.AccessToken, refresh_token = token.RefreshToken, expires_in = token.ExpiresIn, scope = token.Scope });
        }
    }
}
