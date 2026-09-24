using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityFy.Retrieve.MusicBrainz.Migrations
{
    public partial class AddArtistIndexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create index on Name if it does not already exist
            migrationBuilder.Sql(@"
IF NOT EXISTS (
    SELECT name FROM sys.indexes
    WHERE name = 'IX_Artists_Name' AND object_id = OBJECT_ID(N'[dbo].[Artists]')
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Artists_Name] ON [dbo].[Artists] ([Name]);
END
");

            // Create index on MusicBrainzId if it does not already exist
            migrationBuilder.Sql(@"
IF NOT EXISTS (
    SELECT name FROM sys.indexes
    WHERE name = 'IX_Artists_MusicBrainzId' AND object_id = OBJECT_ID(N'[dbo].[Artists]')
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Artists_MusicBrainzId] ON [dbo].[Artists] ([MusicBrainzId]);
END
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF EXISTS (
    SELECT name FROM sys.indexes
    WHERE name = 'IX_Artists_Name' AND object_id = OBJECT_ID(N'[dbo].[Artists]')
)
BEGIN
    DROP INDEX [IX_Artists_Name] ON [dbo].[Artists];
END
");

            migrationBuilder.Sql(@"
IF EXISTS (
    SELECT name FROM sys.indexes
    WHERE name = 'IX_Artists_MusicBrainzId' AND object_id = OBJECT_ID(N'[dbo].[Artists]')
)
BEGIN
    DROP INDEX [IX_Artists_MusicBrainzId] ON [dbo].[Artists];
END
");
        }
    }
}
