namespace LuminaVault.MediaImport;

public class ThumbnailServiceClient(HttpClient httpClient)
{
    public async Task RequestThumbnailAsync(Guid mediaId, string bucket, string storageKey)
    {
        var request = new { MediaId = mediaId, Bucket = bucket, StorageKey = storageKey };
        await httpClient.PostAsJsonAsync("/thumbnails/generate", request);
    }
}
