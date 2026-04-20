namespace LuminaVault.MediaImport;

public class ObjectRecognitionServiceClient(HttpClient httpClient, ILogger<ObjectRecognitionServiceClient> logger)
{
    public async Task RecognizeAsync(Guid mediaId, string contentType, string storageBucket, string storageKey)
    {
        logger.LogInformation("[PIPELINE:Recognition] HTTP-Aufruf → POST /recognize für MediaId={MediaId}, ContentType={ContentType}",
            mediaId, contentType);
        try
        {
            var request = new
            {
                MediaId = mediaId,
                ContentType = contentType,
                StorageBucket = storageBucket,
                StorageKey = storageKey
            };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var response = await httpClient.PostAsJsonAsync("/recognize", request);
            sw.Stop();
            logger.LogInformation("[PIPELINE:Recognition] HTTP-Antwort: {StatusCode} in {ElapsedMs}ms für MediaId={MediaId}",
                response.StatusCode, sw.ElapsedMilliseconds, mediaId);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[PIPELINE:Recognition] FEHLGESCHLAGEN für MediaId={MediaId}", mediaId);
        }
    }
}
