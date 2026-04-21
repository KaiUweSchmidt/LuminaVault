namespace LuminaVault.ThumbnailGeneration;

public record GenerateThumbnailRequest(Guid MediaId, string Bucket, string StorageKey, string? ContentType = null);
