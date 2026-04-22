using System.Text.Json;
using System.Text.Json.Serialization;

namespace LuminaVault.GeocodingService;

/// <summary>
/// Calls the locally hosted Gisgraphy REST API to resolve GPS coordinates into a
/// human-readable location string.
/// </summary>
public sealed class GisgraphyClient(HttpClient httpClient, ILogger<GisgraphyClient> logger)
{
    public async Task<string?> ReverseGeocodeAsync(double latitude, double longitude, CancellationToken cancellationToken = default)
    {
        try
        {
            var latStr = latitude.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var lngStr = longitude.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var url = $"/reversegeocoding/reversegeocode?lat={Uri.EscapeDataString(latStr)}&lng={Uri.EscapeDataString(lngStr)}&format=json&radius=10000";

            var response = await httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("[Gisgraphy] HTTP {StatusCode} bei Reverse-Geocoding-Anfrage", (int)response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<GisgraphyResponse>(json);
            if (result?.Result is not { Count: > 0 } results)
                return null;

            var first = results[0];

            var parts = new List<string>(2);
            if (!string.IsNullOrWhiteSpace(first.Name))
                parts.Add(first.Name);
            if (!string.IsNullOrWhiteSpace(first.CountryName))
                parts.Add(first.CountryName);

            return parts.Count > 0 ? string.Join(", ", parts) : first.FormattedFull;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Gisgraphy] Reverse-Geocoding fehlgeschlagen");
            return null;
        }
    }

    // ── JSON model ────────────────────────────────────────────────────────

    private sealed class GisgraphyResponse
    {
        [JsonPropertyName("result")]
        public List<GisgraphyResult>? Result { get; init; }
    }

    private sealed class GisgraphyResult
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("countryName")]
        public string? CountryName { get; init; }

        [JsonPropertyName("formattedFull")]
        public string? FormattedFull { get; init; }
    }
}
