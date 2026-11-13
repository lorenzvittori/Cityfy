using System.Globalization;
using System.Net;
using System.Text.Json;
using CityFy.Retrieve.LastFm.Mappers;
using CityFy.Retrieve.LastFm.Models;
using CityFy.Retrieve.LastFm.Options;
using Microsoft.Extensions.Options;

namespace CityFy.Retrieve.LastFm.Clients;

public sealed class LastFmClient(
    HttpClient http,
    ILastFmRequestGate gate,
    ILastFmResponseMapper mapper,
    IOptions<LastFmOptions> options,
    ILogger<LastFmClient> logger) : ILastFmClient
{
    private static readonly TimeSpan TransientRetryDelay = TimeSpan.FromSeconds(2);

    private readonly LastFmOptions _options = options.Value;

    public string GetAuthorizeUrl(string callbackUrl) =>
        $"{_options.AuthUrl}?api_key={Uri.EscapeDataString(_options.ApiKey)}&cb={Uri.EscapeDataString(callbackUrl)}";

    public async Task<LastFmSession> GetSessionAsync(string token, CancellationToken ct)
    {
        var root = await SendAsync(LastFmRequest.Of("auth.getSession", ("token", token)), signed: true, ct);
        return mapper.ToSession(root);
    }

    public async Task<LastFmRecentTracksPage> GetRecentTracksAsync(
        string username, string? sessionKey, DateTime? from, DateTime? to, int page, CancellationToken ct)
    {
        // Our bounds are exclusive: one second inside them, whatever Last.fm does at the edge
        var request = LastFmRequest.Of("user.getRecentTracks",
            ("user", username),
            ("limit", _options.PageSize.ToString(CultureInfo.InvariantCulture)),
            ("page", page.ToString(CultureInfo.InvariantCulture)),
            ("extended", "1"),
            ("from", from is { } f ? (Unix(f) + 1).ToString(CultureInfo.InvariantCulture) : null),
            ("to", to is { } t ? (Unix(t) - 1).ToString(CultureInfo.InvariantCulture) : null),
            ("sk", sessionKey));
        var root = await SendAsync(request, signed: sessionKey is not null, ct);
        return mapper.ToRecentTracks(root);
    }

    private async Task<JsonElement> SendAsync(LastFmRequest request, bool signed, CancellationToken ct)
    {
        var withKey = request with
        {
            Parameters = new Dictionary<string, string>(request.Parameters, StringComparer.Ordinal) { ["api_key"] = _options.ApiKey }
        };
        // format is not signed (authspec), so it is appended after the signature is computed
        var query = withKey.ToQueryString(signed ? withKey.Signature(_options.SharedSecret) : null) + "&format=json";
        var url = _options.ApiBase + query;

        for (var attempt = 1; ; attempt++)
        {
            await gate.WaitAsync(ct);
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseContentRead, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            // Last.fm answers errors as {"error": n} with a 4xx/5xx status (sometimes 200)
            var root = TryParse(body);
            var error = root is { } r ? mapper.ToError(r) : null;
            if (error is null && resp.IsSuccessStatusCode && root is { } ok)
            {
                gate.Succeeded();
                return ok;
            }

            if (error?.Code == LastFmApiException.RateLimitExceeded)
                gate.Pause();

            var transient = error?.IsTransient ?? (int)resp.StatusCode >= 500 || resp.StatusCode == HttpStatusCode.TooManyRequests;
            if (transient && attempt < _options.MaxAttempts)
            {
                logger.LogWarning("Last.fm {Method} -> {Status} {Error}, retry {Attempt}/{Max}",
                    request.Method, (int)resp.StatusCode, error?.Code, attempt, _options.MaxAttempts - 1);
                // After a 29 the gate holds the next attempt for the whole pause
                if (error?.Code != LastFmApiException.RateLimitExceeded)
                    await Task.Delay(TransientRetryDelay * attempt, ct);
                continue;
            }

            logger.LogWarning("Last.fm {Method} failed: HTTP {Status}, error {Error} {Message}",
                request.Method, (int)resp.StatusCode, error?.Code, error?.Message);
            throw error is not null
                ? error
                : new HttpRequestException($"Last.fm {request.Method} failed with HTTP {(int)resp.StatusCode}", null, resp.StatusCode);
        }
    }

    private static JsonElement? TryParse(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static long Unix(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();
}
