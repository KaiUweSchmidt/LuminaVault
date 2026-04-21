using LuminaVault.ServiceDefaults;
using Minio;
using Minio.DataModel.Args;
using NATS.Client.Core;

namespace LuminaVault.ObjectRecognition;

/// <summary>
/// Background service that subscribes to the <c>media.imported</c> NATS subject and
/// runs YOLO object recognition (plus face-recognition delegation) for every newly
/// imported image.
/// </summary>
public sealed class NatsObjectRecognitionSubscriber(
    INatsConnection nats,
    IMinioClient minio,
    YoloObjectDetector yolo,
    FaceRecognitionClient faceRecognition,
    MetadataStorageClient metadataStorage,
    ILogger<NatsObjectRecognitionSubscriber> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("[NATS:ObjRec] Subscriber gestartet — wartet auf '{Subject}'", NatsSubjects.MediaImported);

        await foreach (var msg in nats.SubscribeAsync<MediaImportedEvent>(NatsSubjects.MediaImported, cancellationToken: stoppingToken))
        {
            var evt = msg.Data;
            if (evt is null) continue;

            logger.LogInformation("[NATS:ObjRec] Event empfangen: MediaId={MediaId}, ContentType={ContentType}",
                evt.MediaId, evt.ContentType);

            if (!evt.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation("[NATS:ObjRec] Übersprungen (kein Bild): MediaId={MediaId}", evt.MediaId);
                continue;
            }

            try
            {
                await ProcessAsync(evt, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[NATS:ObjRec] Objekterkennung FEHLGESCHLAGEN für MediaId={MediaId}", evt.MediaId);
            }
        }

        logger.LogInformation("[NATS:ObjRec] Subscriber gestoppt");
    }

    private async Task ProcessAsync(MediaImportedEvent evt, CancellationToken ct)
    {
        logger.LogInformation("[NATS:ObjRec] Schritt 1/3: Bild aus MinIO herunterladen für MediaId={MediaId}", evt.MediaId);
        var imageStream = new MemoryStream();
        await minio.GetObjectAsync(new GetObjectArgs()
            .WithBucket(evt.StorageBucket)
            .WithObject(evt.StorageKey)
            .WithCallbackStream(s => s.CopyTo(imageStream)), ct);
        var imageBytes = imageStream.ToArray();

        logger.LogInformation("[NATS:ObjRec] Schritt 2/3: YOLO Objekterkennung für MediaId={MediaId}", evt.MediaId);
        YoloDetectionResult detection;
        try
        {
            detection = yolo.Detect(imageBytes);
            logger.LogInformation("[NATS:ObjRec] YOLO erkannt: [{Objects}], PersonDetected={PersonDetected}",
                string.Join(", ", detection.Objects), detection.PersonDetected);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[NATS:ObjRec] YOLO FEHLGESCHLAGEN für MediaId={MediaId}", evt.MediaId);
            detection = new YoloDetectionResult([], false);
        }

        if (detection.Objects.Count > 0)
        {
            await metadataStorage.UpdateTagsAsync(evt.MediaId, [.. detection.Objects]);
            logger.LogInformation("[NATS:ObjRec] Tags in MetadataStorage aktualisiert für MediaId={MediaId}", evt.MediaId);
        }

        logger.LogInformation("[NATS:ObjRec] Schritt 3/3: Ergebnisse weiterleiten für MediaId={MediaId}", evt.MediaId);
        if (detection.PersonDetected)
        {
            logger.LogInformation("[NATS:ObjRec] Person erkannt → FaceRecognition aufrufen für MediaId={MediaId}", evt.MediaId);
            await faceRecognition.RecognizeFacesAsync(evt.MediaId, evt.StorageBucket, evt.StorageKey);
        }

        logger.LogInformation("[NATS:ObjRec] Objekterkennung abgeschlossen: MediaId={MediaId}", evt.MediaId);
    }
}
