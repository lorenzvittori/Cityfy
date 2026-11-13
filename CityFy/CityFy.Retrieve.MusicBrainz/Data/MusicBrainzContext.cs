using CityFy.Retrieve.MusicBrainz.Models;
using Microsoft.EntityFrameworkCore;

namespace CityFy.Retrieve.MusicBrainz.Data;

public sealed class MusicBrainzContext : DbContext
{
    public MusicBrainzContext(DbContextOptions<MusicBrainzContext> options) : base(options)
    {
    }

    public DbSet<Genre> Genres { get; set; } = null!;
    public DbSet<GenreRelation> GenreRelations { get; set; } = null!;
    public DbSet<Artist> Artists { get; set; } = null!;
    public DbSet<ArtistGenre> ArtistGenres { get; set; } = null!;
    public DbSet<ArtistSpotifyLink> ArtistSpotifyLinks { get; set; } = null!;

    /// <summary>
    /// ICU collation, case-insensitive and accent-sensitive ("Beyonce" = "beyonce" but not "Beyoncé"):
    /// the artist-name lookup used to rely on the case-insensitive default collation of SQL Server.
    /// </summary>
    public const string CaseInsensitiveCollation = "case_insensitive";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasCollation(CaseInsensitiveCollation, locale: "und-u-ks-level2", provider: "icu", deterministic: false);

        // Ids are MusicBrainz row ids: the import writes them as they are (bulk copy)
        modelBuilder.Entity<Genre>(b =>
        {
            b.HasKey(g => g.Id);
            b.Property(g => g.Id).ValueGeneratedNever();
            b.Property(g => g.Gid).IsRequired().HasMaxLength(36);
            b.Property(g => g.Name).IsRequired().HasMaxLength(200);
            b.HasIndex(g => g.Name);
        });

        modelBuilder.Entity<GenreRelation>(b =>
        {
            b.HasKey(r => r.Id);
            b.Property(r => r.RelationType).IsRequired().HasMaxLength(50);
            b.HasOne(r => r.FromGenre).WithMany(g => g.OutgoingRelations)
                .HasForeignKey(r => r.FromGenreId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(r => r.ToGenre).WithMany(g => g.IncomingRelations)
                .HasForeignKey(r => r.ToGenreId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(r => new { r.FromGenreId, r.ToGenreId, r.RelationType }).IsUnique();
        });

        modelBuilder.Entity<Artist>(b =>
        {
            b.HasKey(a => a.Id);
            b.Property(a => a.Id).ValueGeneratedNever();
            b.Property(a => a.Gid).IsRequired().HasMaxLength(36);
            // Lookup by name (dump plays have no artist id): equality ignores the case
            b.Property(a => a.Name).IsRequired().HasMaxLength(500).UseCollation(CaseInsensitiveCollation);
            b.HasIndex(a => a.Name);
            b.HasIndex(a => a.Gid);
        });

        modelBuilder.Entity<ArtistGenre>(b =>
        {
            b.HasKey(ag => new { ag.ArtistId, ag.GenreId });
            b.HasOne(ag => ag.Artist).WithMany(a => a.Genres)
                .HasForeignKey(ag => ag.ArtistId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(ag => ag.Genre).WithMany()
                .HasForeignKey(ag => ag.GenreId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ArtistSpotifyLink>(b =>
        {
            b.HasKey(l => new { l.ArtistId, l.SpotifyArtistId });
            // Spotify ids are base62, i.e. CASE-SENSITIVE: MusicBrainz holds "6D8m8pBW..." and "6d8m8pbw..."
            // as different links. The default PostgreSQL collation compares them like Spotify does
            // (never give this column a case-insensitive collation).
            b.Property(l => l.SpotifyArtistId).IsRequired().HasMaxLength(22);
            b.HasOne(l => l.Artist).WithMany(a => a.SpotifyLinks)
                .HasForeignKey(l => l.ArtistId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(l => l.SpotifyArtistId);
        });
    }
}
