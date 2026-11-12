using CityFy.Retrieve.Spotify.Clients;
using CityFy.Retrieve.Spotify.Models;
using CityFy.Retrieve.Spotify.Repositories;
using Microsoft.AspNetCore.DataProtection;
using ServiceDefault.Errors;

namespace CityFy.Retrieve.Spotify.SubServices;

public sealed class SpotifyAccountLinkSubService(
    ISpotifyClient client,
    ISpotifyAccountRepository repo,
    IDataProtectionProvider dataProtection) : ISpotifyAccountLinkSubService
{
    private readonly IDataProtector _protector = dataProtection.CreateProtector(SpotifyAccessTokenSubService.ProtectorPurpose);

    public async Task<SpotifyAccount> LinkAsync(TokenResponse token, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token.AccessToken) || string.IsNullOrEmpty(token.RefreshToken))
            throw new CityFyException(ErrorCodes.SpotifyTokenExchangeFailed, "Spotify did not return both an access and a refresh token", StatusCodes.Status502BadGateway);

        var user = await client.GetCurrentUserAsync(token.AccessToken, ct);

        var account = new SpotifyAccount
        {
            UserId = user.Id!,
            DisplayName = user.DisplayName,
            ProtectedRefreshToken = _protector.Protect(token.RefreshToken),
            Scope = token.Scope
        };
        await repo.UpsertAsync(account, ct);
        return account;
    }
}