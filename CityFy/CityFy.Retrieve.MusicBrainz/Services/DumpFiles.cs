using System.Text;

namespace CityFy.Retrieve.MusicBrainz.Services;

/// <summary>
/// Archives and tables of the MusicBrainz full export used by the import.
/// Column layouts follow the MusicBrainz schema (CreateTables.sql), see the readers in
/// <see cref="SubServices.MusicBrainzImportSubService"/>.
/// </summary>
public static class DumpFiles
{
    public const string CoreArchive = "mbdump.tar.bz2";
    public const string DerivedArchive = "mbdump-derived.tar.bz2";

    /// <summary>Tables to extract from each archive (tar member paths).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> TablesByArchive = new Dictionary<string, string[]>
    {
        [CoreArchive] = ["mbdump/genre", "mbdump/link_type", "mbdump/link", "mbdump/l_genre_genre",
                         "mbdump/artist", "mbdump/l_artist_url", "mbdump/url"],
        [DerivedArchive] = ["mbdump/tag", "mbdump/artist_tag"],
    };
}
