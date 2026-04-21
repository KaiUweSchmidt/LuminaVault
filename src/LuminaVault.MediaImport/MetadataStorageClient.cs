namespace LuminaVault.MediaImport;

/// <summary>
/// HTTP client for communicating with the MetadataStorage service.
/// </summary>
public class MetadataStorageClient(HttpClient httpClient)
{
    public async Task CreateMetadataAsync(Guid mediaId, string title, string fileName, string contentType,
        long fileSizeBytes, double? gpsLatitude = null, double? gpsLongitude = null, string? gpsLocation = null,
        DateTimeOffset? capturedAt = null)
    {
        var request = new
        {
            MediaId = mediaId,
            FileName = fileName,
            ContentType = contentType,
            FileSizeBytes = fileSizeBytes,
            Title = title,
            Description = $"Imported file: {fileName}",
            Tags = new[] { contentType.Split('/')[0] },
            GpsLatitude = gpsLatitude,
            GpsLongitude = gpsLongitude,
            GpsLocation = gpsLocation,
            CapturedAt = capturedAt
        };

        var response = await httpClient.PostAsJsonAsync("/media", request);
        response.EnsureSuccessStatusCode();
    }
}
