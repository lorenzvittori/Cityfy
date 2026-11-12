using CityFy.Retrieve.Spotify.Options;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace CityFy.Retrieve.Spotify.SubServices;

public sealed class SpotifyAuthorizeUrlSubService(IOptions<SpotifyOptions> options) : ISpotifyAuthorizeUrlSubService
{
    private readonly SpotifyOptions _options = options.Value;

    public string Build(string state) =>
        QueryHelpers.AddQueryString($"{_options.AccountsBase.TrimEnd('/')}/authorize", new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = _options.ClientId,
            ["scope"] = _options.Scopes,
            ["redirect_uri"] = _options.RedirectUri,
            ["state"] = state,
            ["show_dialog"] = "true"
        });
}