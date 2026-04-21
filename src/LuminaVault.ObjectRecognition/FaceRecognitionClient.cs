namespace LuminaVault.ObjectRecognition;

public class FaceRecognitionClient(HttpClient httpClient, ILogger<FaceRecognitionClient> logger)
{
    public async Task RecognizeFacesAsync(Guid mediaId, string storageBucket, string storageKey)
    {
        logger.LogInformation("[PIPELINE:ObjRec] FaceRecognition → POST /recognize für MediaId={MediaId}", mediaId);
        try
        {
            var request = new
            {
                MediaId = mediaId,
                StorageBucket = storageBucket,
                StorageKey = storageKey
            };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var response = await httpClient.PostAsJsonAsync("/recognize", request);
            sw.Stop();
            logger.LogInformation("[PIPELINE:ObjRec] FaceRecognition ← {StatusCode} in {ElapsedMs}ms für MediaId={MediaId}",
                response.StatusCode, sw.ElapsedMilliseconds, mediaId);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[PIPELINE:ObjRec] FaceRecognition FEHLGESCHLAGEN für MediaId={MediaId}", mediaId);
        }
    }
}
