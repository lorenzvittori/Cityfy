using Microsoft.EntityFrameworkCore;
using CityFy.Retrieve.MusicBrainz.Models;

namespace CityFy.Retrieve.MusicBrainz.Data;

public class MusicBrainzContext : DbContext
{
    public MusicBrainzContext(DbContextOptions<MusicBrainzContext> options) : base(options)
    {
    }

    public DbSet<Genre> Genres { get; set; } = null!;
    public DbSet<GenreRelation> GenreRelations { get; set; } = null!;
    public DbSet<Artist> Artists { get; set; } = null!;
    public DbSet<ArtistGenreRelation> ArtistGenreRelations { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Genre>(b =>
        {
            b.HasKey(g => g.Id);
            b.Property(g => g.Name).IsRequired().HasMaxLength(200);
            b.HasIndex(g => g.MusicBrainzId).IsUnique(false);
        });

        modelBuilder.Entity<Artist>(b =>
        {
            b.HasKey(a => a.Id);
            b.Property(a => a.Name).IsRequired().HasMaxLength(300);
            b.HasIndex(a => a.MusicBrainzId).IsUnique(false);
        });

        modelBuilder.Entity<ArtistGenreRelation>(b =>
        {
            b.HasKey(r => r.Id);
            b.HasOne(r => r.Artist)
             .WithMany(a => a.GenreRelations)
             .HasForeignKey(r => r.ArtistId)
             .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(r => r.Genre)
             .WithMany()
             .HasForeignKey(r => r.GenreId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GenreRelation>(b =>
        {
            b.HasKey(gr => gr.Id);
            b.HasOne(gr => gr.Parent)
             .WithMany(g => g.ChildRelations)
             .HasForeignKey(gr => gr.ParentId)
             .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(gr => gr.Child)
             .WithMany(g => g.ParentRelations)
             .HasForeignKey(gr => gr.ChildId)
             .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
