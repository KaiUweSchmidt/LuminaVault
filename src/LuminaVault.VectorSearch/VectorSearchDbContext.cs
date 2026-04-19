using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;

namespace LuminaVault.VectorSearch;

public class VectorSearchDbContext(DbContextOptions<VectorSearchDbContext> options) : DbContext(options)
{
    public DbSet<MediaEmbedding> MediaEmbeddings => Set<MediaEmbedding>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.Entity<MediaEmbedding>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.MediaId).IsUnique();
            entity.Property(e => e.Embedding).HasColumnType("vector(512)");
            entity.HasIndex(e => e.Embedding)
                .HasMethod("ivfflat")
                .HasOperators("vector_l2_ops")
                .HasStorageParameter("lists", 100);
        });
    }
}
