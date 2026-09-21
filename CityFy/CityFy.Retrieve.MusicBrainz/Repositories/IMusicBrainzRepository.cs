using CityFy.Retrieve.MusicBrainz.Models;

namespace CityFy.Retrieve.MusicBrainz.Repositories;

public interface IMusicBrainzRepository
{
    Task InsertArtistTagsAsync(ArtistTags artistTags);
    Task InsertRelatedTagsAsync(TagGraph graph);

    // Read operations
    Task<TagGraph?> GetTagGraphByIdAsync(string id);
    Task<IEnumerable<TagGraph>> GetTagGraphsPagedAsync(int page, int pageSize);
}
