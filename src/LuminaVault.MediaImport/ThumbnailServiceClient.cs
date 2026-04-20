namespace LuminaVault.MediaImport;

public class ThumbnailServiceClient(HttpClient httpClient, ILogger<ThumbnailServiceClient> logger)
{
    public async Task RequestThumbnailAsync(Guid mediaId, string bucket, string storageKey)
    {
        logger.LogInformation("[PIPELINE:Thumbnail] HTTP-Aufruf → POST /thumbnails/generate für MediaId={MediaId}", mediaId);
        var request = new { MediaId = mediaId, Bucket = bucket, StorageKey = storageKey };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var response = await httpClient.PostAsJsonAsync("/thumbnails/generate", request);
        sw.Stop();
        logger.LogInformation("[PIPELINE:Thumbnail] HTTP-Antwort: {StatusCode} in {ElapsedMs}ms für MediaId={MediaId}",
            response.StatusCode, sw.ElapsedMilliseconds, mediaId);
        response.EnsureSuccessStatusCode();
    }
}
