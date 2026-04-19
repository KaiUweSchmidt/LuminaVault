using Microsoft.EntityFrameworkCore;

namespace LuminaVault.MetadataStorage;

public class MetadataDbContext(DbContextOptions<MetadataDbContext> options) : DbContext(options)
{
    public DbSet<MediaMetadata> MediaMetadata => Set<MediaMetadata>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MediaMetadata>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(512);
            entity.Property(e => e.Description).HasMaxLength(4096);
            entity.Property(e => e.Tags).HasColumnType("text[]");
        });
    }
}
