namespace LuminaVault.ServiceDefaults;

/// <summary>
/// NATS reply message returned by the geocoding service after resolving GPS coordinates.
/// <see cref="LocationName"/> is <c>null</c> when geocoding failed or no result was found.
/// </summary>
public sealed record ReverseGeocodingReply(string? LocationName);
