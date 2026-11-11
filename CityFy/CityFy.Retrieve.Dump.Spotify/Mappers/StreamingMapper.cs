using System;
using System.Collections.Generic;
using System.Linq;
using CityFy.Retrieve.Dump.Spotify.Models;
using ServiceDefault.Listening;

namespace CityFy.Retrieve.Dump.Spotify.Mappers
{
    public class StreamingMapper : IStreamingMapper
    {
        public IEnumerable<StreamingBl> MapToBl(IEnumerable<StreamingDto> dtos)
        {
            return dtos.Select(MapToBl);
        }

        public StreamingBl MapToBl(StreamingDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));

            return new StreamingBl
            {
                Timestamp = dto.Ts,
                Platform = dto.Platform,
                MsPlayed = dto.MsPlayed ?? 0,
                Country = dto.ConnCountry,
                IpAddress = dto.IpAddr,
                TrackName = dto.TrackName,
                ArtistName = dto.ArtistName,
                AlbumName = dto.AlbumName,
                SpotifyTrackUri = dto.SpotifyTrackUri,
                ReasonStart = dto.ReasonStart,
                ReasonEnd = dto.ReasonEnd,
                Shuffle = dto.Shuffle ?? false,
                Skipped = dto.Skipped ?? false,
                Offline = dto.Offline ?? false,
                OfflineTimestamp = dto.OfflineTimestamp,
                IncognitoMode = dto.IncognitoMode ?? false
            };
        }

        public ListeningEvent? MapToListeningEvent(StreamingBl bl, string userId, string uploadId)
        {
            if (bl == null) throw new ArgumentNullException(nameof(bl));

            // Only music: episodes/audiobooks have no spotify:track URI
            if (string.IsNullOrWhiteSpace(bl.SpotifyTrackUri) ||
                !bl.SpotifyTrackUri.StartsWith("spotify:track:", StringComparison.Ordinal))
                return null;

            // Platform, IP address and country are deliberately not carried over (personal data
            // not needed downstream).
            return new ListeningEvent
            {
                UserId = userId,
                PlayedAt = DateTime.SpecifyKind(bl.Timestamp, DateTimeKind.Utc),
                MsPlayed = bl.MsPlayed,
                Skipped = bl.Skipped,
                TrackUri = bl.SpotifyTrackUri,
                TrackName = bl.TrackName,
                ArtistName = bl.ArtistName,
                AlbumName = bl.AlbumName,
                SourceRef = uploadId
            };
        }
    }
}
