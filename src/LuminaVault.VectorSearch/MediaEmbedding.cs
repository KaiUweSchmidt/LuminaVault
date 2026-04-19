using Pgvector;

namespace LuminaVault.VectorSearch;

public class MediaEmbedding
{
    public Guid Id { get; set; }
    public Guid MediaId { get; set; }
    public Vector Embedding { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public record StoreEmbeddingRequest(Guid MediaId, float[] Embedding);
public record SearchRequest(float[] Embedding, int TopK = 10);
public record SearchResult(Guid MediaId, double Distance);
