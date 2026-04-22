using LuminaVault.ServiceDefaults;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using Minio;
using Minio.DataModel.Args;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace LuminaVault.GeocodingService;

/// <summary>
/// Background service that consumes <c>media.imported</c> events from JetStream,
/// extracts GPS coordinates from image EXIF data, reverse-geocodes via Gisgraphy,
/// and updates MetadataStorage with the resolved location.
/// </summary>
public sealed class NatsGeocodingSubscriber(
    INatsConnection nats,
    INatsJSContext js,
    IMinioClient minio,
    GisgraphyClient gisgraphy,
    GeocodingMetadataClient metadataStorage,
    ILogger<NatsGeocodingSubscriber> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("[NATS:Geocoding] JetStream consumer starten — Stream={Stream}, Consumer={Consumer}",
            NatsStreams.MediaPipeline, NatsConsumers.Geocoding);

        await Extensions.EnsureJetStreamResourcesAsync(js);

        var stream = await js.GetStreamAsync(NatsStreams.MediaPipeline, cancellationToken: stoppingToken);
        var consumer = await stream.CreateOrUpdateConsumerAsync(
            new ConsumerConfig(NatsConsumers.Geocoding)
            {
                FilterSubject = NatsSubjects.MediaImported,
                AckWait = TimeSpan.FromMinutes(2),
                MaxDeliver = 3,
                AckPolicy = ConsumerConfigAckPolicy.Explicit,
            }, stoppingToken);

        logger.LogInformation("[NATS:Geocoding] Consumer bereit — wartet auf Messages");

        await foreach (var msg in consumer.ConsumeAsync<MediaImportedEvent>(cancellationToken: stoppingToken))
        {
            var evt = msg.Data;
            if (evt is null)
            {
                await msg.AckAsync(cancellationToken: stoppingToken);
                continue;
            }

            logger.LogInformation("[NATS:Geocoding] Event empfangen: MediaId={MediaId}, ContentType={ContentType}",
                evt.MediaId, evt.ContentType);

            if (!evt.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation("[NATS:Geocoding] Übersprungen (kein Bild): MediaId={MediaId}", evt.MediaId);
                await msg.AckAsync(cancellationToken: stoppingToken);
                await PublishStepCompletedAsync(evt.MediaId, PipelineSteps.Geocoding, 0, true);
                continue;
            }

            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                await ProcessAsync(evt, stoppingToken);
                sw.Stop();
                await msg.AckAsync(cancellationToken: stoppingToken);
                await PublishStepCompletedAsync(evt.MediaId, PipelineSteps.Geocoding, sw.ElapsedMilliseconds, true);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[NATS:Geocoding] Geocoding FEHLGESCHLAGEN für MediaId={MediaId}", evt.MediaId);
                await msg.NakAsync(cancellationToken: stoppingToken);
                await PublishStepCompletedAsync(evt.MediaId, PipelineSteps.Geocoding, 0, false);
            }
        }

        logger.LogInformation("[NATS:Geocoding] Subscriber gestoppt");
    }

    private async Task ProcessAsync(MediaImportedEvent evt, CancellationToken ct)
    {
        logger.LogInformation("[NATS:Geocoding] Bild aus MinIO herunterladen für MediaId={MediaId}", evt.MediaId);
        using var imageStream = new MemoryStream();
        await minio.GetObjectAsync(new GetObjectArgs()
            .WithBucket(evt.StorageBucket)
            .WithObject(evt.StorageKey)
            .WithCallbackStream(s => s.CopyTo(imageStream)), ct);
        imageStream.Position = 0;

        var gpsCoords = ExtractGps(imageStream);
        if (gpsCoords is null)
        {
            logger.LogInformation("[NATS:Geocoding] Keine GPS-Daten in EXIF gefunden für MediaId={MediaId}", evt.MediaId);
            return;
        }

        var (latitude, longitude) = gpsCoords.Value;
        logger.LogInformation("[NATS:Geocoding] GPS-Koordinaten: ({Lat}, {Lon}) für MediaId={MediaId}",
            latitude, longitude, evt.MediaId);

        // Also extract capture date
        imageStream.Position = 0;
        var capturedAt = ExtractDateTaken(imageStream);

        var locationName = await gisgraphy.ReverseGeocodeAsync(latitude, longitude, ct);
        if (string.IsNullOrWhiteSpace(locationName))
        {
            logger.LogWarning("[NATS:Geocoding] Reverse-Geocoding lieferte kein Ergebnis für MediaId={MediaId} – Koordinaten werden ohne Ortsnamen gespeichert", evt.MediaId);
        }
        else
        {
            logger.LogInformation("[NATS:Geocoding] Ort ermittelt: '{Location}' für MediaId={MediaId}",
                locationName, evt.MediaId);
        }

        await metadataStorage.UpdateGpsAsync(evt.MediaId, latitude, longitude, locationName, capturedAt);
        logger.LogInformation("[NATS:Geocoding] MetadataStorage aktualisiert für MediaId={MediaId}", evt.MediaId);
    }

    private static (double Latitude, double Longitude)? ExtractGps(Stream imageStream)
    {
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(imageStream);
            var gps = directories.OfType<GpsDirectory>().FirstOrDefault();
            var location = gps?.GetGeoLocation();
            return location is not null ? (location.Latitude, location.Longitude) : null;
        }
        catch
        {
            return null;
        }
    }

    private static DateTimeOffset? ExtractDateTaken(Stream imageStream)
    {
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(imageStream);
            var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            if (subIfd is null) return null;

            if (subIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dateTime))
                return new DateTimeOffset(dateTime, TimeSpan.Zero);

            if (subIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeDigitized, out dateTime))
                return new DateTimeOffset(dateTime, TimeSpan.Zero);

            return null;
        }
        catch
        {
            return null;
        }
    }

    private async Task PublishStepCompletedAsync(Guid mediaId, string stepName, long durationMs, bool success)
    {
        try
        {
            var stepEvent = new PipelineStepCompletedEvent(mediaId, stepName, durationMs, success, DateTimeOffset.UtcNow);
            await nats.PublishAsync(NatsSubjects.PipelineStepCompleted, stepEvent);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[NATS:Geocoding] Step-Completion-Event konnte nicht veröffentlicht werden: {Step}", stepName);
        }
    }
}
