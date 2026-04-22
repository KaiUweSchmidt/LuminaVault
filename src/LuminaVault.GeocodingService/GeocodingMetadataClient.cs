namespace LuminaVault.GeocodingService;

/// <summary>
/// HTTP client for updating GPS metadata in the MetadataStorage service.
/// </summary>
public sealed class GeocodingMetadataClient(HttpClient httpClient, ILogger<GeocodingMetadataClient> logger)
{
    /// <summary>
    /// Updates GPS coordinates and location name for a media item.
    /// </summary>
    public async Task UpdateGpsAsync(Guid mediaId, double latitude, double longitude, string? locationName, DateTimeOffset? capturedAt)
    {
        logger.LogInformation("[Geocoding:MetaClient] UpdateGps → PUT /media/{MediaId}/gps", mediaId);
        try
        {
            var request = new
            {
                GpsLatitude = latitude,
                GpsLongitude = longitude,
                GpsLocation = locationName,
                CapturedAt = capturedAt
            };
            var response = await httpClient.PutAsJsonAsync($"/media/{mediaId}/gps", request);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Geocoding:MetaClient] UpdateGps FEHLGESCHLAGEN für MediaId={MediaId}", mediaId);
        }
    }
}
