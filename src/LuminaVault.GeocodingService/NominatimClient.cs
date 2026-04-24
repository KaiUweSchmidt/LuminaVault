using System.Text.Json;
using System.Text.Json.Serialization;

namespace LuminaVault.GeocodingService;

/// <summary>
/// Calls the locally hosted Nominatim API to resolve GPS coordinates into a
/// human-readable location string.
/// </summary>
public sealed class NominatimClient(HttpClient httpClient, ILogger<NominatimClient> logger)
{
    public async Task<string?> ReverseGeocodeAsync(double latitude, double longitude, CancellationToken cancellationToken = default)
    {
        try
        {
            var latStr = latitude.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var lonStr = longitude.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var url = $"/reverse?lat={latStr}&lon={lonStr}&format=jsonv2&accept-language=de";

            var response = await httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("[Nominatim] HTTP {StatusCode} bei Reverse-Geocoding-Anfrage", (int)response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogDebug("[Nominatim] Antwort für ({Lat}, {Lon}): {Json}", latStr, lonStr, json);

            var result = JsonSerializer.Deserialize<NominatimResponse>(json);
            if (result?.Error is not null)
            {
                logger.LogWarning("[Nominatim] Fehler: {Error}", result.Error);
                return null;
            }

            if (result?.Address is not { } address)
                return result?.DisplayName;

            var city = address.City ?? address.Town ?? address.Village ?? address.Municipality ?? address.County;
            var parts = new List<string>(2);
            if (!string.IsNullOrWhiteSpace(city))
                parts.Add(city);
            if (!string.IsNullOrWhiteSpace(address.Country))
                parts.Add(address.Country);

            return parts.Count > 0 ? string.Join(", ", parts) : result.DisplayName;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Nominatim] Reverse-Geocoding fehlgeschlagen");
            return null;
        }
    }

    // ── JSON model ──────────────────────────────────────────────────────

    private sealed class NominatimResponse
    {
        [JsonPropertyName("display_name")]
        public string? DisplayName { get; init; }

        [JsonPropertyName("error")]
        public string? Error { get; init; }

        [JsonPropertyName("address")]
        public NominatimAddress? Address { get; init; }
    }

    private sealed class NominatimAddress
    {
        [JsonPropertyName("city")]
        public string? City { get; init; }

        [JsonPropertyName("town")]
        public string? Town { get; init; }

        [JsonPropertyName("village")]
        public string? Village { get; init; }

        [JsonPropertyName("municipality")]
        public string? Municipality { get; init; }

        [JsonPropertyName("county")]
        public string? County { get; init; }

        [JsonPropertyName("country")]
        public string? Country { get; init; }
    }
}
