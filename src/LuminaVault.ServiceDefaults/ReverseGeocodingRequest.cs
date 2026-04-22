namespace LuminaVault.ServiceDefaults;

/// <summary>
/// NATS request message sent to the geocoding service to resolve GPS coordinates
/// into a human-readable location name.
/// </summary>
public sealed record ReverseGeocodingRequest(double Latitude, double Longitude);
