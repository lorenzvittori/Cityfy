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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Genre>(b =>
        {
            b.HasKey(g => g.Id);
            b.Property(g => g.Name).IsRequired().HasMaxLength(200);
            b.HasIndex(g => g.MusicBrainzId).IsUnique(false);
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
