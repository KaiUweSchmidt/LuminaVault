using Microsoft.EntityFrameworkCore;

namespace LuminaVault.AiTagging;

public class AiTaggingDbContext(DbContextOptions<AiTaggingDbContext> options) : DbContext(options)
{
    public DbSet<TaggingResult> TaggingResults => Set<TaggingResult>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TaggingResult>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.MediaId);
            entity.Property(e => e.Tags).HasColumnType("text[]");
        });
    }
}
