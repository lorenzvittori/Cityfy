using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityFy.Retrieve.MusicBrainz.Migrations
{
    /// <summary>
    /// SpotifyArtistId becomes case-sensitive (binary collation): Spotify ids are base62 and
    /// MusicBrainz holds links that differ only by case, which collided on the primary key with the
    /// default case-insensitive collation. SQL Server cannot change the collation of a primary-key
    /// column, so the key is dropped and recreated around the change (EF handles the index).
    /// </summary>
    public partial class SpotifyIdCaseSensitive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(name: "PK_ArtistSpotifyLinks", table: "ArtistSpotifyLinks");

            migrationBuilder.AlterColumn<string>(
                name: "SpotifyArtistId",
                table: "ArtistSpotifyLinks",
                type: "varchar(22)",
                unicode: false,
                maxLength: 22,
                nullable: false,
                collation: "Latin1_General_100_BIN2",
                oldClrType: typeof(string),
                oldType: "varchar(22)",
                oldUnicode: false,
                oldMaxLength: 22);

            migrationBuilder.AddPrimaryKey(
                name: "PK_ArtistSpotifyLinks",
                table: "ArtistSpotifyLinks",
                columns: new[] { "ArtistId", "SpotifyArtistId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(name: "PK_ArtistSpotifyLinks", table: "ArtistSpotifyLinks");

            migrationBuilder.AlterColumn<string>(
                name: "SpotifyArtistId",
                table: "ArtistSpotifyLinks",
                type: "varchar(22)",
                unicode: false,
                maxLength: 22,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(22)",
                oldUnicode: false,
                oldMaxLength: 22,
                oldCollation: "Latin1_General_100_BIN2");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ArtistSpotifyLinks",
                table: "ArtistSpotifyLinks",
                columns: new[] { "ArtistId", "SpotifyArtistId" });
        }
    }
}
