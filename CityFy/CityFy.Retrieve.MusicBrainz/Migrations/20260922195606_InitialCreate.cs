using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityFy.Retrieve.MusicBrainz.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Genres",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MusicBrainzId = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Genres", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GenreRelations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ParentId = table.Column<int>(type: "int", nullable: false),
                    ChildId = table.Column<int>(type: "int", nullable: false),
                    RelationType = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GenreRelations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GenreRelations_Genres_ChildId",
                        column: x => x.ChildId,
                        principalTable: "Genres",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GenreRelations_Genres_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Genres",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GenreRelations_ChildId",
                table: "GenreRelations",
                column: "ChildId");

            migrationBuilder.CreateIndex(
                name: "IX_GenreRelations_ParentId",
                table: "GenreRelations",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Genres_MusicBrainzId",
                table: "Genres",
                column: "MusicBrainzId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GenreRelations");

            migrationBuilder.DropTable(
                name: "Genres");
        }
    }
}
