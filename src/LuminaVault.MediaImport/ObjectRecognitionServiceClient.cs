namespace LuminaVault.MediaImport;

public class ObjectRecognitionServiceClient(HttpClient httpClient, ILogger<ObjectRecognitionServiceClient> logger)
{
    public async Task RecognizeAsync(Guid mediaId, string contentType, string storageBucket, string storageKey)
    {
        try
        {
            var request = new
            {
                MediaId = mediaId,
                ContentType = contentType,
                StorageBucket = storageBucket,
                StorageKey = storageKey
            };
            var response = await httpClient.PostAsJsonAsync("/recognize", request);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Object recognition failed for media {MediaId}", mediaId);
        }
    }
}
