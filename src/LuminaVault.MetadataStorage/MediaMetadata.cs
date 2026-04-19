namespace LuminaVault.MetadataStorage;

public class MediaMetadata
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string[] Tags { get; set; } = [];
    public double? GpsLatitude { get; set; }
    public double? GpsLongitude { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public record CreateMediaMetadataRequest(
    Guid MediaId,
    string Title,
    string Description,
    string[] Tags,
    double? GpsLatitude,
    double? GpsLongitude);

public record UpdateMediaMetadataRequest(
    string? Title,
    string? Description,
    string[]? Tags);
