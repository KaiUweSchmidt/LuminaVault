using Microsoft.EntityFrameworkCore;

namespace LuminaVault.MetadataStorage;

public class MetadataDbContext(DbContextOptions<MetadataDbContext> options) : DbContext(options)
{
    public DbSet<MediaMetadata> MediaMetadata => Set<MediaMetadata>();
    public DbSet<Face> Faces => Set<Face>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MediaMetadata>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(512);
            entity.Property(e => e.Description).HasMaxLength(4096);
            entity.Property(e => e.Tags).HasColumnType("text[]");
        });

        modelBuilder.Entity<Face>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.MediaId);
            entity.Property(e => e.Name).HasMaxLength(256);
            entity.Property(e => e.FaceDescription).HasMaxLength(4096);
        });
    }
}
