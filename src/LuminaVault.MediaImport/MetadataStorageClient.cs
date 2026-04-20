namespace LuminaVault.MediaImport;

/// <summary>
/// HTTP client for communicating with the MetadataStorage service.
/// </summary>
public class MetadataStorageClient(HttpClient httpClient)
{
    public async Task CreateMetadataAsync(Guid mediaId, string title, string fileName, string contentType)
    {
        var request = new
        {
            MediaId = mediaId,
            Title = title,
            Description = $"Imported file: {fileName}",
            Tags = new[] { contentType.Split('/')[0] },
            GpsLatitude = (double?)null,
            GpsLongitude = (double?)null
        };

        var response = await httpClient.PostAsJsonAsync("/media", request);
        response.EnsureSuccessStatusCode();
    }
}
