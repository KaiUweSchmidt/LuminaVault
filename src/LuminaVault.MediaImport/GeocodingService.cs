using System.Text.Json;
using System.Text.Json.Serialization;

namespace LuminaVault.MediaImport;

/// <summary>
/// Converts GPS coordinates into a human-readable location string.
/// </summary>
public interface IGeocodingService
{
    /// <summary>
    /// Returns a location name for the given coordinates, or null if the lookup fails.
    /// </summary>
    Task<string?> GetLocationNameAsync(double latitude, double longitude, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reverse-geocoding implementation that uses the OpenStreetMap Nominatim API.
/// </summary>
public class NominatimGeocodingService(HttpClient httpClient, ILogger<NominatimGeocodingService> logger) : IGeocodingService
{
    public async Task<string?> GetLocationNameAsync(double latitude, double longitude, CancellationToken cancellationToken = default)
    {
        try
        {
            var latStr = Uri.EscapeDataString(latitude.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var lonStr = Uri.EscapeDataString(longitude.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var url = $"https://nominatim.openstreetmap.org/reverse?lat={latStr}&lon={lonStr}&format=json";
            var response = await httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<NominatimResponse>(json);
            if (result is null) return null;

            // Build a short, readable location string: "City, Country" or just the display_name if city is not available
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(result.Address?.City))
                parts.Add(result.Address.City);
            else if (!string.IsNullOrWhiteSpace(result.Address?.Town))
                parts.Add(result.Address.Town);
            else if (!string.IsNullOrWhiteSpace(result.Address?.Village))
                parts.Add(result.Address.Village);
            else if (!string.IsNullOrWhiteSpace(result.Address?.County))
                parts.Add(result.Address.County);

            if (!string.IsNullOrWhiteSpace(result.Address?.Country))
                parts.Add(result.Address.Country);

            if (parts.Count > 0)
                return string.Join(", ", parts);

            // Fall back to the full display name truncated to a reasonable length
            return result.DisplayName is { Length: > 0 } dn
                ? dn[..Math.Min(dn.Length, 512)]
                : null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[GPS] Reverse geocoding fehlgeschlagen für ({Lat}, {Lon})", latitude, longitude);
            return null;
        }
    }

    private sealed class NominatimResponse
    {
        [JsonPropertyName("display_name")]
        public string? DisplayName { get; init; }

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

        [JsonPropertyName("county")]
        public string? County { get; init; }

        [JsonPropertyName("country")]
        public string? Country { get; init; }
    }
}
