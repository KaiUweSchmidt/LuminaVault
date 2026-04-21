namespace LuminaVault.ObjectRecognition;

public class AiTaggingClient(HttpClient httpClient, ILogger<AiTaggingClient> logger)
{
    public async Task StoreTagsAsync(Guid mediaId, IEnumerable<string> tags)
    {
        logger.LogInformation("[PIPELINE:ObjRec] AiTagging → POST /tags für MediaId={MediaId}", mediaId);
        try
        {
            var request = new
            {
                MediaId = mediaId,
                Tags = tags
            };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var response = await httpClient.PostAsJsonAsync("/tags", request);
            sw.Stop();
            logger.LogInformation("[PIPELINE:ObjRec] AiTagging ← {StatusCode} in {ElapsedMs}ms für MediaId={MediaId}",
                response.StatusCode, sw.ElapsedMilliseconds, mediaId);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[PIPELINE:ObjRec] AiTagging FEHLGESCHLAGEN für MediaId={MediaId}", mediaId);
        }
    }
}
