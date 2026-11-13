using System.Collections.Generic;
using CityFy.Retrieve.Dump.Spotify.Models;
using ServiceDefault.Listening;

namespace CityFy.Retrieve.Dump.Spotify.Mappers;

public interface IStreamingMapper
{
    StreamingBl MapToBl(StreamingDto dto);
    IEnumerable<StreamingBl> MapToBl(IEnumerable<StreamingDto> dtos);

    /// <summary>
    /// Maps a dump entry to the shared listening event. Returns null for entries that are
    /// not music plays (podcast episodes, audiobooks, entries without a track URI).
    /// </summary>
    ListeningEvent? MapToListeningEvent(StreamingBl bl, string userId, string uploadId);
}
