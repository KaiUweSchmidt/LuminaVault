namespace LuminaVault.ServiceDefaults;

/// <summary>
/// Event published via NATS after a media file has been successfully imported.
/// </summary>
public sealed record MediaImportedEvent(
    Guid MediaId,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    string StorageBucket,
    string StorageKey);
