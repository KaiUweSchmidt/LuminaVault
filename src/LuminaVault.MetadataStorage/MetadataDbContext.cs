using Microsoft.EntityFrameworkCore;

namespace LuminaVault.MetadataStorage;

public class MetadataDbContext(DbContextOptions<MetadataDbContext> options) : DbContext(options)
{
    public DbSet<MediaMetadata> MediaMetadata => Set<MediaMetadata>();
    public DbSet<Face> Faces => Set<Face>();
    public DbSet<Collection> Collections => Set<Collection>();
    public DbSet<CollectionMediaItem> CollectionMediaItems => Set<CollectionMediaItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MediaMetadata>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(512);
            entity.Property(e => e.Description).HasMaxLength(4096);
            entity.Property(e => e.Tags).HasColumnType("text[]");
            entity.Property(e => e.GpsLocation).HasMaxLength(512);
        });

        modelBuilder.Entity<Face>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.MediaId);
            entity.Property(e => e.Name).HasMaxLength(256);
            entity.Property(e => e.FaceDescription).HasMaxLength(4096);
        });

        modelBuilder.Entity<Collection>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(256);
            entity.Property(e => e.Description).HasColumnType("text");
            entity.HasMany(e => e.MediaItems)
                  .WithOne(e => e.Collection)
                  .HasForeignKey(e => e.CollectionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CollectionMediaItem>(entity =>
        {
            entity.HasKey(e => new { e.CollectionId, e.MediaId });
            entity.HasIndex(e => e.MediaId);
        });
    }
}
