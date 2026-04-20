namespace LuminaVault.ObjectRecognition;

public class MetadataStorageClient(HttpClient httpClient, ILogger<MetadataStorageClient> logger)
{
    public async Task UpdatePersonCountAsync(Guid mediaId, int personCount)
    {
        try
        {
            var request = new { PersonCount = personCount };
            var response = await httpClient.PutAsJsonAsync($"/media/{mediaId}", request);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to update person count for media {MediaId}", mediaId);
        }
    }

    public async Task StoreFaceAsync(Guid mediaId, string faceDescription)
    {
        try
        {
            var request = new { MediaId = mediaId, FaceDescription = faceDescription };
            var response = await httpClient.PostAsJsonAsync("/faces", request);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to store face for media {MediaId}", mediaId);
        }
    }
}
