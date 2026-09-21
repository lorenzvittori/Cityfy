using CityFy.Retrieve.MusicBrainz.Models;

namespace CityFy.Retrieve.MusicBrainz.Repositories;

public interface IMusicBrainzRepository
{
    Task InsertArtistTagsAsync(ArtistTags artistTags);
    Task InsertRelatedTagsAsync(TagGraph graph);
}
