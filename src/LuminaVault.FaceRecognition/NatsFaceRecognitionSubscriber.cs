using LuminaVault.ServiceDefaults;
using Minio;
using Minio.DataModel.Args;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace LuminaVault.FaceRecognition;

/// <summary>
/// Background service that consumes <c>media.imported</c> events from JetStream
/// with a durable consumer, runs YOLO face detection + Ollama description,
/// and stores results in MetadataStorage.
/// </summary>
public sealed class NatsFaceRecognitionSubscriber(
    INatsConnection nats,
    INatsJSContext js,
    IMinioClient minio,
    YoloFaceDetector yolo,
    OllamaClient ollama,
    MetadataStorageClient metadataStorage,
    ILogger<NatsFaceRecognitionSubscriber> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("[NATS:FaceRec] JetStream consumer starten — Stream={Stream}, Consumer={Consumer}",
            NatsStreams.MediaPipeline, NatsConsumers.FaceRecognition);

        await Extensions.EnsureJetStreamResourcesAsync(js);

        var stream = await js.GetStreamAsync(NatsStreams.MediaPipeline, cancellationToken: stoppingToken);
        var consumer = await stream.CreateOrUpdateConsumerAsync(
            new ConsumerConfig(NatsConsumers.FaceRecognition)
            {
                FilterSubject = NatsSubjects.MediaImported,
                AckWait = TimeSpan.FromMinutes(15),
                MaxDeliver = 3,
                AckPolicy = ConsumerConfigAckPolicy.Explicit,
            }, stoppingToken);

        logger.LogInformation("[NATS:FaceRec] Consumer bereit — wartet auf Messages");

        await foreach (var msg in consumer.ConsumeAsync<MediaImportedEvent>(cancellationToken: stoppingToken))
        {
            var evt = msg.Data;
            if (evt is null)
            {
                await msg.AckAsync(cancellationToken: stoppingToken);
                continue;
            }

            logger.LogInformation("[NATS:FaceRec] Event empfangen: MediaId={MediaId}, ContentType={ContentType}",
                evt.MediaId, evt.ContentType);

            if (!evt.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation("[NATS:FaceRec] Übersprungen (kein Bild): MediaId={MediaId}", evt.MediaId);
                await msg.AckAsync(cancellationToken: stoppingToken);
                await PublishStepCompletedAsync(evt.MediaId, PipelineSteps.FaceRecognition, 0, true);
                continue;
            }

            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                await ProcessAsync(evt, stoppingToken);
                sw.Stop();
                await msg.AckAsync(cancellationToken: stoppingToken);
                await PublishStepCompletedAsync(evt.MediaId, PipelineSteps.FaceRecognition, sw.ElapsedMilliseconds, true);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[NATS:FaceRec] Gesichtserkennung FEHLGESCHLAGEN für MediaId={MediaId}", evt.MediaId);
                await msg.NakAsync(cancellationToken: stoppingToken);
                await PublishStepCompletedAsync(evt.MediaId, PipelineSteps.FaceRecognition, 0, false);
            }
        }

        logger.LogInformation("[NATS:FaceRec] Subscriber gestoppt");
    }

    private async Task ProcessAsync(MediaImportedEvent evt, CancellationToken ct)
    {
        logger.LogInformation("[NATS:FaceRec] Schritt 1/3: Bild aus MinIO herunterladen für MediaId={MediaId}", evt.MediaId);
        using var imageStream = new MemoryStream();
        await minio.GetObjectAsync(new GetObjectArgs()
            .WithBucket(evt.StorageBucket)
            .WithObject(evt.StorageKey)
            .WithCallbackStream(s => s.CopyTo(imageStream)), ct);
        var imageBytes = imageStream.ToArray();
        var base64Image = Convert.ToBase64String(imageBytes);

        logger.LogInformation("[NATS:FaceRec] Schritt 2/3: YOLO-Face Erkennung für MediaId={MediaId}", evt.MediaId);
        List<DetectedFace> detectedFaces;
        try
        {
            detectedFaces = yolo.DetectFaces(imageBytes);
            logger.LogInformation("[NATS:FaceRec] YOLO hat {FaceCount} Gesicht(er) erkannt für MediaId={MediaId}",
                detectedFaces.Count, evt.MediaId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[NATS:FaceRec] YOLO FEHLGESCHLAGEN für MediaId={MediaId}", evt.MediaId);
            detectedFaces = [];
        }

        var personCount = detectedFaces.Count;
        await metadataStorage.UpdatePersonCountAsync(evt.MediaId, personCount);

        if (personCount > 0)
        {
            logger.LogInformation("[NATS:FaceRec] Schritt 3/3: {PersonCount} Gesicht(er) mit Ollama beschreiben für MediaId={MediaId}",
                personCount, evt.MediaId);

            for (var i = 0; i < detectedFaces.Count; i++)
            {
                var detected = detectedFaces[i];
                var description = await ollama.DescribeFaceAsync(base64Image, i, personCount);
                await metadataStorage.StoreFaceAsync(evt.MediaId, description,
                    detected.BboxX, detected.BboxY, detected.BboxWidth, detected.BboxHeight);
                logger.LogInformation("[NATS:FaceRec] Gesicht {Index}/{Total}: Gespeichert", i + 1, personCount);
            }
        }
        else
        {
            logger.LogInformation("[NATS:FaceRec] Schritt 3/3: Übersprungen (keine Gesichter erkannt)");
        }

        logger.LogInformation("[NATS:FaceRec] Abgeschlossen: MediaId={MediaId}, Personen={PersonCount}", evt.MediaId, personCount);
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
            logger.LogWarning(ex, "[NATS:FaceRec] Step-Completion-Event konnte nicht veröffentlicht werden: {Step}", stepName);
        }
    }
}
