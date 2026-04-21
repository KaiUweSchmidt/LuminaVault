namespace LuminaVault.ObjectRecognition;

/// <summary>
/// HTTP client for updating media metadata (e.g. tags) in the MetadataStorage service.
/// </summary>
public class MetadataStorageClient(HttpClient httpClient, ILogger<MetadataStorageClient> logger)
{
    /// <summary>
    /// Updates the tags for a media item in MetadataStorage.
    /// </summary>
    public async Task UpdateTagsAsync(Guid mediaId, string[] tags)
    {
        logger.LogInformation("[PIPELINE:ObjRec] MetadataStorage → PUT /media/{MediaId} (Tags=[{Tags}])",
            mediaId, string.Join(", ", tags));
        try
        {
            var request = new { Tags = tags };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var response = await httpClient.PutAsJsonAsync($"/media/{mediaId}", request);
            sw.Stop();
            logger.LogInformation("[PIPELINE:ObjRec] MetadataStorage ← {StatusCode} in {ElapsedMs}ms",
                response.StatusCode, sw.ElapsedMilliseconds);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[PIPELINE:ObjRec] MetadataStorage Tags-Update FEHLGESCHLAGEN für MediaId={MediaId}", mediaId);
        }
    }
}
