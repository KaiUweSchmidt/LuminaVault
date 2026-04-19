namespace LuminaVault.AiTagging;

public class TaggingResult
{
    public Guid Id { get; set; }
    public Guid MediaId { get; set; }
    public string[] Tags { get; set; } = [];
    public float Confidence { get; set; }
    public DateTimeOffset AnalyzedAt { get; set; }
}

public record AnalyzeRequest(Guid MediaId, string ContentType, string StorageKey);
