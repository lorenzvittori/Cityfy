using System.Collections.Generic;
using CityFy.Retrieve.Dump.Spotify.Models;

namespace CityFy.Retrieve.Dump.Spotify.Mappers
{
    public interface IStreamingMapper
    {
        StreamingBl MapToBl(StreamingDto dto);
        StreamingMongo MapToMongo(StreamingBl bl, string uploadId);
        IEnumerable<StreamingBl> MapToBl(IEnumerable<StreamingDto> dtos);
    }
}
