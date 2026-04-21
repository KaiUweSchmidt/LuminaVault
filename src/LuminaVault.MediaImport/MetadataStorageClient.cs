namespace LuminaVault.MediaImport;

/// <summary>
/// HTTP client for communicating with the MetadataStorage service.
/// </summary>
public class MetadataStorageClient(HttpClient httpClient)
{
    public async Task CreateMetadataAsync(Guid mediaId, string title, string fileName, string contentType,
        double? gpsLatitude = null, double? gpsLongitude = null, string? gpsLocation = null)
    {
        var request = new
        {
            MediaId = mediaId,
            Title = title,
            Description = $"Imported file: {fileName}",
            Tags = new[] { contentType.Split('/')[0] },
            GpsLatitude = gpsLatitude,
            GpsLongitude = gpsLongitude,
            GpsLocation = gpsLocation
        };

        var response = await httpClient.PostAsJsonAsync("/media", request);
        response.EnsureSuccessStatusCode();
    }
}
