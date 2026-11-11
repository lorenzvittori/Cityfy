using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityFy.Retrieve.MusicBrainz.Migrations
{
    /// <summary>
    /// Rebuilds the schema for the column-based import. The previous data was produced by a
    /// parser that read the wrong columns (genre names were GUIDs, no genre relations, no
    /// artist genres), so it is dropped: run POST api/retrieve afterwards to re-import.
    /// Ids become the MusicBrainz row ids, which SQL Server cannot do by altering an IDENTITY
    /// column: the tables are dropped and recreated.
    /// </summary>
    public partial class RebuildForColumnBasedImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Old schema, children first
            migrationBuilder.DropTable(name: "ArtistGenreRelations");
            migrationBuilder.DropTable(name: "GenreRelations");
            migrationBuilder.DropTable(name: "Artists");
            migrationBuilder.DropTable(name: "Genres");

            migrationBuilder.CreateTable(
                name: "Genres",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Gid = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_Genres", x => x.Id));

            migrationBuilder.CreateTable(
                name: "Artists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Gid = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    TotalGenreVotes = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_Artists", x => x.Id));

            migrationBuilder.CreateTable(
                name: "GenreRelations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FromGenreId = table.Column<int>(type: "int", nullable: false),
                    ToGenreId = table.Column<int>(type: "int", nullable: false),
                    RelationType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GenreRelations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GenreRelations_Genres_FromGenreId",
                        column: x => x.FromGenreId,
                        principalTable: "Genres",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GenreRelations_Genres_ToGenreId",
                        column: x => x.ToGenreId,
                        principalTable: "Genres",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ArtistGenres",
                columns: table => new
                {
                    ArtistId = table.Column<int>(type: "int", nullable: false),
                    GenreId = table.Column<int>(type: "int", nullable: false),
                    Votes = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArtistGenres", x => new { x.ArtistId, x.GenreId });
                    table.ForeignKey(
                        name: "FK_ArtistGenres_Artists_ArtistId",
                        column: x => x.ArtistId,
                        principalTable: "Artists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ArtistGenres_Genres_GenreId",
                        column: x => x.GenreId,
                        principalTable: "Genres",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ArtistSpotifyLinks",
                columns: table => new
                {
                    ArtistId = table.Column<int>(type: "int", nullable: false),
                    SpotifyArtistId = table.Column<string>(type: "varchar(22)", unicode: false, maxLength: 22, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArtistSpotifyLinks", x => new { x.ArtistId, x.SpotifyArtistId });
                    table.ForeignKey(
                        name: "FK_ArtistSpotifyLinks_Artists_ArtistId",
                        column: x => x.ArtistId,
                        principalTable: "Artists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(name: "IX_Genres_Name", table: "Genres", column: "Name");
            migrationBuilder.CreateIndex(name: "IX_Artists_Gid", table: "Artists", column: "Gid");
            migrationBuilder.CreateIndex(name: "IX_Artists_Name", table: "Artists", column: "Name");
            migrationBuilder.CreateIndex(name: "IX_GenreRelations_ToGenreId", table: "GenreRelations", column: "ToGenreId");
            migrationBuilder.CreateIndex(
                name: "IX_GenreRelations_FromGenreId_ToGenreId_RelationType",
                table: "GenreRelations",
                columns: new[] { "FromGenreId", "ToGenreId", "RelationType" },
                unique: true);
            migrationBuilder.CreateIndex(name: "IX_ArtistGenres_GenreId", table: "ArtistGenres", column: "GenreId");
            migrationBuilder.CreateIndex(name: "IX_ArtistSpotifyLinks_SpotifyArtistId", table: "ArtistSpotifyLinks", column: "SpotifyArtistId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The previous schema only held data produced by the broken parser: nothing to go back to.
            throw new NotSupportedException("RebuildForColumnBasedImport cannot be reverted: drop the database and re-run the migrations instead.");
        }
    }
}
