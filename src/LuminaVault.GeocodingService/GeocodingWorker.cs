using LuminaVault.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using NATS.Client.Core;

namespace LuminaVault.GeocodingService;

/// <summary>
/// Background service that subscribes to <c>geocoding.reverse</c> NATS requests,
/// resolves coordinates via the local Gisgraphy instance, and replies with a
/// <see cref="ReverseGeocodingReply"/>.  Results are cached in PostgreSQL so that
/// repeated lookups for the same location never hit Gisgraphy twice.
/// </summary>
public sealed class GeocodingWorker(
    INatsConnection nats,
    GisgraphyClient gisgraphy,
    IServiceScopeFactory scopeFactory,
    ILogger<GeocodingWorker> logger) : BackgroundService
{
    /// <summary>
    /// Coordinates are rounded to 3 decimal places before cache lookup (~111 m grid).
    /// </summary>
    private const int CoordinatePrecision = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("[Geocoding] Worker gestartet — wartet auf '{Subject}'", NatsSubjects.ReverseGeocodingRequest);

        await foreach (var msg in nats.SubscribeAsync<ReverseGeocodingRequest>(
                           NatsSubjects.ReverseGeocodingRequest,
                           cancellationToken: stoppingToken))
        {
            if (msg.Data is not { } request)
                continue;

            logger.LogDebug("[Geocoding] Anfrage empfangen");

            string? locationName;
            try
            {
                locationName = await ResolveWithCacheAsync(request.Latitude, request.Longitude, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Geocoding] Fehler bei der Auflösung");
                locationName = null;
            }

            // Only reply when a reply subject was provided (standard NATS request/reply).
            if (msg.ReplyTo is not null)
            {
                await msg.ReplyAsync(new ReverseGeocodingReply(locationName), cancellationToken: stoppingToken);
            }

            logger.LogInformation("[Geocoding] Ergebnis: '{Location}'", locationName ?? "<null>");
        }

        logger.LogInformation("[Geocoding] Worker gestoppt");
    }

    private async Task<string?> ResolveWithCacheAsync(double latitude, double longitude, CancellationToken ct)
    {
        var latR = Math.Round(latitude, CoordinatePrecision);
        var lonR = Math.Round(longitude, CoordinatePrecision);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GeocodingDbContext>();

        // Try cache first
        var cached = await db.GeocodingCache
            .FirstOrDefaultAsync(e => e.LatitudeRounded == latR && e.LongitudeRounded == lonR, ct);

        if (cached is not null)
        {
            logger.LogDebug("[Geocoding] Cache-Treffer: '{Location}'", cached.LocationName);
            return cached.LocationName;
        }

        // Resolve via Gisgraphy
        var locationName = await gisgraphy.ReverseGeocodeAsync(latitude, longitude, ct);

        // Persist to cache
        db.GeocodingCache.Add(new GeocodingCacheEntry
        {
            LatitudeRounded = latR,
            LongitudeRounded = lonR,
            LocationName = locationName,
            CachedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(ct);

        return locationName;
    }
}
