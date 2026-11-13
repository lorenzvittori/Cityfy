using System.Text.Json;
using CityFy.Retrieve.LastFm.Models;

namespace CityFy.Retrieve.LastFm.Mappers;

/// <summary>Reads the JSON of the Last.fm API (format=json).</summary>
public interface ILastFmResponseMapper
{
    /// <summary>The API error in the body ({"error": 29, "message": ...}), null when the body is a result.</summary>
    LastFmApiException? ToError(JsonElement root);

    /// <summary>auth.getSession. Throws <see cref="InvalidDataException"/> when name or key are missing.</summary>
    LastFmSession ToSession(JsonElement root);

    /// <summary>user.getRecentTracks. Throws <see cref="InvalidDataException"/> when "recenttracks" is missing.</summary>
    LastFmRecentTracksPage ToRecentTracks(JsonElement root);
}
