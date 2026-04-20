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
    public int? PersonCount { get; set; }
}

public class Face
{
    public Guid Id { get; set; }
    public Guid MediaId { get; set; }
    public string? Name { get; set; }
    public string FaceDescription { get; set; } = string.Empty;
    public DateTimeOffset DetectedAt { get; set; }
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
    string[]? Tags,
    int? PersonCount = null);

public record CreateFaceRequest(
    Guid MediaId,
    string FaceDescription,
    string? Name = null);

public record UpdateFaceNameRequest(string? Name);
