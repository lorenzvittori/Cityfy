using System.Security.Cryptography;
using System.Text;
using CityFy.Api.Auth;
using CityFy.Api.Models;
using CityFy.Api.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ServiceDefault.Errors;

namespace CityFy.Api.Controllers;

[ApiController]
[AdminEnabled]
[ApiExplorerSettings(GroupName = "admin")]
[Route("api/admin")]
public sealed class AdminAuthController(IOptions<AdminOptions> options) : ControllerBase
{
    // POST api/admin/login — mock admin user from configuration (Admin:Username / Admin:Password)
    [HttpPost("login")]
    public async Task<ActionResult<AdminUserDto>> Login([FromBody] AdminLoginRequest request)
    {
        var o = options.Value;
        // Non-short-circuit '|': both comparisons always run (no timing hint on which one failed)
        if (!FixedTimeEquals(request.Username, o.Username) | !FixedTimeEquals(request.Password, o.Password))
            return this.CityFyProblem(ErrorCodes.AdminInvalidCredentials, "Invalid admin credentials", StatusCodes.Status401Unauthorized);

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, AuthSetup.CreateAdminPrincipal(o.Username));
        return Ok(new AdminUserDto(o.Username));
    }

    // GET api/admin/me — is the current session an admin?
    [Authorize(Policy = AuthSetup.AdminPolicy)]
    [HttpGet("me")]
    public ActionResult<AdminUserDto> Me() => Ok(new AdminUserDto(User.Identity?.Name));

    // POST api/admin/logout
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    private static bool FixedTimeEquals(string? a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a ?? ""), Encoding.UTF8.GetBytes(b));
}
