using LuminaVault.ServiceDefaults;
using Minio;
using Minio.DataModel.Args;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace LuminaVault.ObjectRecognition;

/// <summary>
/// Background service that consumes <c>media.imported</c> events from JetStream
/// with a durable consumer and explicit Ack/Nak, ensuring no messages are lost
/// even during long-running Ollama inference or service restarts.
/// </summary>
public sealed class NatsObjectRecognitionSubscriber(
    INatsConnection nats,
    INatsJSContext js,
    IMinioClient minio,
    YoloObjectDetector yolo,
    FaceRecognitionClient faceRecognition,
    MetadataStorageClient metadataStorage,
    ILogger<NatsObjectRecognitionSubscriber> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("[NATS:ObjRec] JetStream consumer starten — Stream={Stream}, Consumer={Consumer}",
            NatsStreams.MediaPipeline, NatsConsumers.ObjectRecognition);

        await Extensions.EnsureJetStreamResourcesAsync(js);

        var stream = await js.GetStreamAsync(NatsStreams.MediaPipeline, cancellationToken: stoppingToken);
        var consumer = await stream.CreateOrUpdateConsumerAsync(
            new ConsumerConfig(NatsConsumers.ObjectRecognition)
            {
                FilterSubject = NatsSubjects.MediaImported,
                AckWait = TimeSpan.FromMinutes(15),
                MaxDeliver = 3,
                AckPolicy = ConsumerConfigAckPolicy.Explicit,
            }, stoppingToken);

        logger.LogInformation("[NATS:ObjRec] Consumer bereit — wartet auf Messages");

        await foreach (var msg in consumer.ConsumeAsync<MediaImportedEvent>(cancellationToken: stoppingToken))
        {
            var evt = msg.Data;
            if (evt is null)
            {
                await msg.AckAsync(cancellationToken: stoppingToken);
                continue;
            }

            logger.LogInformation("[NATS:ObjRec] Event empfangen: MediaId={MediaId}, ContentType={ContentType}",
                evt.MediaId, evt.ContentType);

            if (!evt.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation("[NATS:ObjRec] Übersprungen (kein Bild): MediaId={MediaId}", evt.MediaId);
                await msg.AckAsync(cancellationToken: stoppingToken);
                await PublishStepCompletedAsync(evt.MediaId, PipelineSteps.ObjectRecognition, 0, true);
                continue;
            }

            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                await ProcessAsync(evt, stoppingToken);
                sw.Stop();
                await msg.AckAsync(cancellationToken: stoppingToken);
                await PublishStepCompletedAsync(evt.MediaId, PipelineSteps.ObjectRecognition, sw.ElapsedMilliseconds, true);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[NATS:ObjRec] Objekterkennung FEHLGESCHLAGEN für MediaId={MediaId}", evt.MediaId);
                await msg.NakAsync(cancellationToken: stoppingToken);
                await PublishStepCompletedAsync(evt.MediaId, PipelineSteps.ObjectRecognition, 0, false);
            }
        }

        logger.LogInformation("[NATS:ObjRec] Subscriber gestoppt");
    }

    private async Task ProcessAsync(MediaImportedEvent evt, CancellationToken ct)
    {
        logger.LogInformation("[NATS:ObjRec] Schritt 1/3: Bild aus MinIO herunterladen für MediaId={MediaId}", evt.MediaId);
        using var imageStream = new MemoryStream();
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

    private async Task PublishStepCompletedAsync(Guid mediaId, string stepName, long durationMs, bool success)
    {
        try
        {
            var stepEvent = new PipelineStepCompletedEvent(mediaId, stepName, durationMs, success, DateTimeOffset.UtcNow);
            await nats.PublishAsync(NatsSubjects.PipelineStepCompleted, stepEvent);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[NATS:ObjRec] Step-Completion-Event konnte nicht veröffentlicht werden: {Step}", stepName);
        }
    }
}
