using System;
using System.Collections.Generic;
using System.Linq;
using CityFy.Retrieve.Dump.Spotify.Models;

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

        public StreamingMongo MapToMongo(StreamingBl bl, string uploadId)
        {
            if (bl == null) throw new ArgumentNullException(nameof(bl));

            return new StreamingMongo
            {
                Timestamp = bl.Timestamp,
                Platform = bl.Platform,
                MsPlayed = bl.MsPlayed,
                Country = bl.Country,
                IpAddress = bl.IpAddress,
                TrackName = bl.TrackName,
                ArtistName = bl.ArtistName,
                AlbumName = bl.AlbumName,
                SpotifyTrackUri = bl.SpotifyTrackUri,
                ReasonStart = bl.ReasonStart,
                ReasonEnd = bl.ReasonEnd,
                Shuffle = bl.Shuffle,
                Skipped = bl.Skipped,
                Offline = bl.Offline,
                OfflineTimestamp = bl.OfflineTimestamp,
                IncognitoMode = bl.IncognitoMode,
                UploadId = uploadId
            };
        }
    }
}
