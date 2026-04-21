using LuminaVault.ServiceDefaults;
using Minio;
using Minio.DataModel.Args;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace LuminaVault.ThumbnailGeneration;

/// <summary>
/// Background service that consumes <c>media.imported</c> events from JetStream
/// with a durable consumer and generates thumbnails for every newly imported media file.
/// </summary>
public sealed class NatsThumbnailSubscriber(
    INatsConnection nats,
    INatsJSContext js,
    IMinioClient minio,
    ILogger<NatsThumbnailSubscriber> logger) : BackgroundService
{
    private const string ThumbnailBucket = "thumbnails";
    private const int ThumbnailWidth = 320;
    private const int ThumbnailHeight = 240;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("[NATS:Thumbnails] JetStream consumer starten — Stream={Stream}, Consumer={Consumer}",
            NatsStreams.MediaPipeline, NatsConsumers.ThumbnailGeneration);

        await EnsureBucketExistsAsync(ThumbnailBucket);
        await Extensions.EnsureJetStreamResourcesAsync(js);

        var stream = await js.GetStreamAsync(NatsStreams.MediaPipeline, cancellationToken: stoppingToken);
        var consumer = await stream.CreateOrUpdateConsumerAsync(
            new ConsumerConfig(NatsConsumers.ThumbnailGeneration)
            {
                FilterSubject = NatsSubjects.MediaImported,
                AckWait = TimeSpan.FromMinutes(2),
                MaxDeliver = 3,
                AckPolicy = ConsumerConfigAckPolicy.Explicit,
            }, stoppingToken);

        logger.LogInformation("[NATS:Thumbnails] Consumer bereit — wartet auf Messages");

        await foreach (var msg in consumer.ConsumeAsync<MediaImportedEvent>(cancellationToken: stoppingToken))
        {
            var evt = msg.Data;
            if (evt is null)
            {
                await msg.AckAsync(cancellationToken: stoppingToken);
                continue;
            }

            logger.LogInformation("[NATS:Thumbnails] Event empfangen: MediaId={MediaId}, ContentType={ContentType}",
                evt.MediaId, evt.ContentType);

            var isImageOrVideo = evt.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                              || evt.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase);
            if (!isImageOrVideo)
            {
                logger.LogInformation("[NATS:Thumbnails] Übersprungen (kein Bild/Video): MediaId={MediaId}", evt.MediaId);
                await msg.AckAsync(cancellationToken: stoppingToken);
                await PublishStepCompletedAsync(evt.MediaId, PipelineSteps.ThumbnailGeneration, 0, true);
                continue;
            }

            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                await GenerateThumbnailAsync(evt, stoppingToken);
                sw.Stop();
                await msg.AckAsync(cancellationToken: stoppingToken);
                await PublishStepCompletedAsync(evt.MediaId, PipelineSteps.ThumbnailGeneration, sw.ElapsedMilliseconds, true);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[NATS:Thumbnails] Thumbnail-Generierung FEHLGESCHLAGEN für MediaId={MediaId}", evt.MediaId);
                await msg.NakAsync(cancellationToken: stoppingToken);
                await PublishStepCompletedAsync(evt.MediaId, PipelineSteps.ThumbnailGeneration, 0, false);
            }
        }

        logger.LogInformation("[NATS:Thumbnails] Subscriber gestoppt");
    }

    private async Task GenerateThumbnailAsync(MediaImportedEvent evt, CancellationToken ct)
    {
        using var sourceStream = new MemoryStream();
        await minio.GetObjectAsync(new GetObjectArgs()
            .WithBucket(evt.StorageBucket)
            .WithObject(evt.StorageKey)
            .WithCallbackStream(s => s.CopyTo(sourceStream)), ct);
        sourceStream.Position = 0;

        MemoryStream outputStream;
        if (evt.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
        {
            outputStream = await ExtractVideoFrameAsync(sourceStream, ThumbnailWidth, ThumbnailHeight, evt.MediaId);
        }
        else
        {
            using var image = await Image.LoadAsync(sourceStream, ct);
            image.Mutate(ctx => ctx.Resize(new ResizeOptions
            {
                Size = new Size(ThumbnailWidth, ThumbnailHeight),
                Mode = ResizeMode.Max
            }));

            outputStream = new MemoryStream();
            await image.SaveAsJpegAsync(outputStream, ct);
            outputStream.Position = 0;
        }

        await using (outputStream)
        {
            var thumbnailKey = $"{evt.MediaId}/thumb.jpg";
            await minio.PutObjectAsync(new PutObjectArgs()
                .WithBucket(ThumbnailBucket)
                .WithObject(thumbnailKey)
                .WithStreamData(outputStream)
                .WithObjectSize(outputStream.Length)
                .WithContentType("image/jpeg"), ct);

            logger.LogInformation("[NATS:Thumbnails] Thumbnail erstellt: MediaId={MediaId}, Key={Key}", evt.MediaId, thumbnailKey);
        }
    }

    private async Task EnsureBucketExistsAsync(string bucketName)
    {
        var exists = await minio.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucketName));
        if (!exists)
            await minio.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucketName));
    }

    private async Task<MemoryStream> ExtractVideoFrameAsync(Stream videoStream, int width, int height, Guid mediaId)
    {
        var tempInput = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.tmp");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.jpg");
        try
        {
            await using (var fs = File.Create(tempInput))
            {
                await videoStream.CopyToAsync(fs);
            }

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-i \"{tempInput}\" -vf \"select=eq(n\\,14),scale={width}:{height}:force_original_aspect_ratio=decrease\" -frames:v 1 -y \"{tempOutput}\"",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(psi)!;
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0 || !File.Exists(tempOutput))
            {
                logger.LogWarning("[NATS:Thumbnails] FFmpeg exited with code {ExitCode} for MediaId={MediaId}: {Stderr}",
                    process.ExitCode, mediaId, stderr);
                throw new InvalidOperationException($"FFmpeg failed with exit code {process.ExitCode}");
            }

            var result = new MemoryStream();
            await using (var outFs = File.OpenRead(tempOutput))
            {
                await outFs.CopyToAsync(result);
            }
            result.Position = 0;
            return result;
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
            if (File.Exists(tempOutput)) File.Delete(tempOutput);
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
            logger.LogWarning(ex, "[NATS:Thumbnails] Step-Completion-Event konnte nicht veröffentlicht werden: {Step}", stepName);
        }
    }
}
