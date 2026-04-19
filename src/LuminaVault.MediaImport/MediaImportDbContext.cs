using Microsoft.EntityFrameworkCore;

namespace LuminaVault.MediaImport;

public class MediaImportDbContext(DbContextOptions<MediaImportDbContext> options) : DbContext(options)
{
    public DbSet<MediaItem> MediaItems => Set<MediaItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MediaItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FileName).IsRequired().HasMaxLength(512);
            entity.Property(e => e.ContentType).IsRequired().HasMaxLength(256);
            entity.Property(e => e.StorageBucket).IsRequired().HasMaxLength(256);
            entity.Property(e => e.StorageKey).IsRequired().HasMaxLength(1024);
        });
    }
}
