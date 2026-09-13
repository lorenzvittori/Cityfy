namespace CityFy.RtrieveSpotify.Models
{
    public class PagedResult<T>
    {
        public IEnumerable<T> Items { get; set; } = Array.Empty<T>();
        // The numeric 'before' cursor to pass to the next request (nullable)
        public long? Before { get; set; }
        // Raw next url as returned by Spotify (optional, for debugging)
        public string? NextUrl { get; set; }
    }
}
