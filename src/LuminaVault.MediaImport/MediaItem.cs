namespace LuminaVault.MediaImport;

public class MediaItem
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public DateTimeOffset ImportedAt { get; set; }
    public string StorageBucket { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
}

public record ImportMediaRequest(string FileName, string ContentType, long FileSizeBytes);
