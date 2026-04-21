using LuminaVault.ObjectRecognition;
using Microsoft.Extensions.Http.Resilience;
using Minio;
using Minio.DataModel.Args;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNatsClient();

builder.Services.AddSingleton<IMinioClient>(sp =>
{
    var config = builder.Configuration.GetSection("Minio");
    return new MinioClient()
        .WithEndpoint(config["Endpoint"])
        .WithCredentials(config["AccessKey"], config["SecretKey"])
        .Build();
});

builder.Services.AddSingleton<YoloObjectDetector>(sp =>
{
    var modelPath = builder.Configuration["Yolo:ModelPath"] ?? "/app/models/yolov8m.onnx";
    var logger = sp.GetRequiredService<ILogger<YoloObjectDetector>>();
    return new YoloObjectDetector(modelPath, logger);
});

builder.Services.AddHttpClient<FaceRecognitionClient>(client =>
    client.BaseAddress = new Uri(builder.Configuration["Services:FaceRecognition"]
        ?? "http://face-recognition:8080"))
    .ConfigureAdditionalHttpMessageHandlers((_, _) => { })
    .AddStandardResilienceHandler(options =>
    {
        options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(5);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(10);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(10);
    });

builder.Services.AddHttpClient<MetadataStorageClient>(client =>
    client.BaseAddress = new Uri(builder.Configuration["Services:MetadataStorage"]
        ?? "http://metadata-storage:8080"));

builder.Services.AddHostedService<NatsObjectRecognitionSubscriber>();

var app = builder.Build();

app.MapDefaultEndpoints();

app.Logger.LogInformation("[PIPELINE:ObjRec] ===== ObjectRecognition Service gestartet — YOLO Objekterkennung =====");

app.MapPost("/recognize", async (
    RecognizeRequest request,
    IMinioClient minio,
    YoloObjectDetector yolo,
    FaceRecognitionClient faceRecognition,
    MetadataStorageClient metadataStorage,
    ILogger<Program> logger) =>
{
    logger.LogInformation("[PIPELINE:ObjRec] ===== Objekterkennung gestartet für MediaId={MediaId} =====", request.MediaId);
    logger.LogInformation("[PIPELINE:ObjRec] ContentType={ContentType}, Bucket={Bucket}, Key={Key}",
        request.ContentType, request.StorageBucket, request.StorageKey);

    if (!request.ContentType.StartsWith("image/"))
    {
        logger.LogInformation("[PIPELINE:ObjRec] Übersprungen: Kein Bildformat ({ContentType})", request.ContentType);
        return Results.Ok(new RecognizeResponse(request.MediaId, false, []));
    }

    byte[] imageBytes;
    try
    {
        logger.LogInformation("[PIPELINE:ObjRec] Schritt 1/3: Bild aus MinIO herunterladen...");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var imageStream = new MemoryStream();
        await minio.GetObjectAsync(new GetObjectArgs()
            .WithBucket(request.StorageBucket)
            .WithObject(request.StorageKey)
            .WithCallbackStream(stream => stream.CopyTo(imageStream)));
        imageBytes = imageStream.ToArray();
        sw.Stop();
        logger.LogInformation("[PIPELINE:ObjRec] Schritt 1/3: Bild heruntergeladen ({SizeKb}KB) in {ElapsedMs}ms",
            imageBytes.Length / 1024, sw.ElapsedMilliseconds);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[PIPELINE:ObjRec] Schritt 1/3: FEHLER beim Download von {StorageKey}", request.StorageKey);
        return Results.Problem("Failed to download image for object recognition");
    }

    logger.LogInformation("[PIPELINE:ObjRec] Schritt 2/3: YOLO Objekterkennung...");
    YoloDetectionResult detection;
    try
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        detection = yolo.Detect(imageBytes);
        sw.Stop();
        logger.LogInformation("[PIPELINE:ObjRec] Schritt 2/3: YOLO hat [{Objects}] erkannt in {ElapsedMs}ms (PersonDetected={PersonDetected})",
            string.Join(", ", detection.Objects), sw.ElapsedMilliseconds, detection.PersonDetected);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[PIPELINE:ObjRec] Schritt 2/3: YOLO Objekterkennung FEHLGESCHLAGEN");
        detection = new YoloDetectionResult([], false);
    }

    var detectedObjects = detection.Objects;
    var personDetected = detection.PersonDetected;

    // Update tags in MetadataStorage with all detected objects
    if (detectedObjects.Count > 0)
    {
        logger.LogInformation("[PIPELINE:ObjRec] Tags in MetadataStorage aktualisieren: [{Tags}] für MediaId={MediaId}",
            string.Join(", ", detectedObjects), request.MediaId);
        await metadataStorage.UpdateTagsAsync(request.MediaId, [.. detectedObjects]);
    }

    logger.LogInformation("[PIPELINE:ObjRec] Schritt 3/3: Ergebnisse weiterleiten...");

    if (personDetected)
    {
        logger.LogInformation("[PIPELINE:ObjRec] Person erkannt → FaceRecognition aufrufen für MediaId={MediaId}", request.MediaId);
        await faceRecognition.RecognizeFacesAsync(request.MediaId, request.StorageBucket, request.StorageKey);
    }

    logger.LogInformation("[PIPELINE:ObjRec] ===== Objekterkennung abgeschlossen: MediaId={MediaId}, PersonDetected={PersonDetected}, Objekte=[{Objects}] =====",
        request.MediaId, personDetected, string.Join(", ", detectedObjects));
    return Results.Ok(new RecognizeResponse(request.MediaId, personDetected, detectedObjects));
});

app.Run();
