namespace LuminaVault.MetadataStorage;

public class MediaMetadata
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string[] Tags { get; set; } = [];
    public double? GpsLatitude { get; set; }
    public double? GpsLongitude { get; set; }
    public string? GpsLocation { get; set; }
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
    public double BboxX { get; set; }
    public double BboxY { get; set; }
    public double BboxWidth { get; set; }
    public double BboxHeight { get; set; }
    public DateTimeOffset DetectedAt { get; set; }
}

public record CreateMediaMetadataRequest(
    Guid MediaId,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    string Title,
    string Description,
    string[] Tags,
    double? GpsLatitude,
    double? GpsLongitude,
    string? GpsLocation = null);

public record UpdateMediaMetadataRequest(
    string? Title,
    string? Description,
    string[]? Tags,
    int? PersonCount = null);

public record CreateFaceRequest(
    Guid MediaId,
    string FaceDescription,
    string? Name = null,
    double BboxX = 0,
    double BboxY = 0,
    double BboxWidth = 0,
    double BboxHeight = 0);

public record UpdateFaceNameRequest(string? Name);
