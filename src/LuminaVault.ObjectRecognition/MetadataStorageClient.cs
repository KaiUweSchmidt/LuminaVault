namespace LuminaVault.ObjectRecognition;

public class MetadataStorageClient(HttpClient httpClient, ILogger<MetadataStorageClient> logger)
{
    public async Task UpdatePersonCountAsync(Guid mediaId, int personCount)
    {
        logger.LogInformation("[PIPELINE:MetaClient] UpdatePersonCount → PUT /media/{MediaId} (PersonCount={PersonCount})",
            mediaId, personCount);
        try
        {
            var request = new { PersonCount = personCount };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var response = await httpClient.PutAsJsonAsync($"/media/{mediaId}", request);
            sw.Stop();
            logger.LogInformation("[PIPELINE:MetaClient] UpdatePersonCount ← {StatusCode} in {ElapsedMs}ms",
                response.StatusCode, sw.ElapsedMilliseconds);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[PIPELINE:MetaClient] UpdatePersonCount FEHLGESCHLAGEN für MediaId={MediaId}", mediaId);
        }
    }

    public async Task StoreFaceAsync(Guid mediaId, string faceDescription)
    {
        logger.LogInformation("[PIPELINE:MetaClient] StoreFace → POST /faces für MediaId={MediaId} ({DescLen} Zeichen)",
            mediaId, faceDescription.Length);
        try
        {
            var request = new { MediaId = mediaId, FaceDescription = faceDescription };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var response = await httpClient.PostAsJsonAsync("/faces", request);
            sw.Stop();
            logger.LogInformation("[PIPELINE:MetaClient] StoreFace ← {StatusCode} in {ElapsedMs}ms",
                response.StatusCode, sw.ElapsedMilliseconds);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[PIPELINE:MetaClient] StoreFace FEHLGESCHLAGEN für MediaId={MediaId}", mediaId);
        }
    }
}
