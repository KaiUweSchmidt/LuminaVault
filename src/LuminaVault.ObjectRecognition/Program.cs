using LuminaVault.ObjectRecognition;
using Minio;
using Minio.DataModel.Args;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddSingleton<IMinioClient>(sp =>
{
    var config = builder.Configuration.GetSection("Minio");
    return new MinioClient()
        .WithEndpoint(config["Endpoint"])
        .WithCredentials(config["AccessKey"], config["SecretKey"])
        .Build();
});

builder.Services.AddHttpClient<OllamaClient>((sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var ollamaUrl = config["Services:Ollama"] ?? "http://ollama:11434";
    client.BaseAddress = new Uri(ollamaUrl);
});

builder.Services.AddHttpClient<MetadataStorageClient>(client =>
    client.BaseAddress = new Uri(builder.Configuration["Services:MetadataStorage"]
        ?? "http://metadata-storage:8080"));

builder.Services.AddSingleton<YoloFaceDetector>(sp =>
{
    var modelPath = builder.Configuration["Yolo:ModelPath"] ?? "/app/models/yolov12m-face.onnx";
    var logger = sp.GetRequiredService<ILogger<YoloFaceDetector>>();
    return new YoloFaceDetector(modelPath, logger);
});

var app = builder.Build();

app.MapDefaultEndpoints();

app.Logger.LogInformation("[PIPELINE:ObjRec] ===== ObjectRecognition Service gestartet — YOLO-Face + Ollama =====");

app.MapPost("/recognize", async (
    RecognizeRequest request,
    IMinioClient minio,
    YoloFaceDetector yolo,
    OllamaClient ollama,
    MetadataStorageClient metadataStorage,
    ILogger<Program> logger) =>
{
    logger.LogInformation("[PIPELINE:ObjRec] ===== Recognition gestartet für MediaId={MediaId} =====", request.MediaId);
    logger.LogInformation("[PIPELINE:ObjRec] ContentType={ContentType}, Bucket={Bucket}, Key={Key}",
        request.ContentType, request.StorageBucket, request.StorageKey);

    if (!request.ContentType.StartsWith("image/"))
    {
        logger.LogInformation("[PIPELINE:ObjRec] Übersprungen: Kein Bildformat ({ContentType})", request.ContentType);
        return Results.Ok(new RecognizeResponse(request.MediaId, 0, []));
    }

    byte[] imageBytes;
    string base64Image;
    try
    {
        logger.LogInformation("[PIPELINE:ObjRec] Schritt 1/4: Bild aus MinIO herunterladen...");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var imageStream = new MemoryStream();
        await minio.GetObjectAsync(new GetObjectArgs()
            .WithBucket(request.StorageBucket)
            .WithObject(request.StorageKey)
            .WithCallbackStream(stream => stream.CopyTo(imageStream)));
        imageBytes = imageStream.ToArray();
        base64Image = Convert.ToBase64String(imageBytes);
        sw.Stop();
        logger.LogInformation("[PIPELINE:ObjRec] Schritt 1/4: Bild heruntergeladen ({SizeKb}KB) in {ElapsedMs}ms",
            imageBytes.Length / 1024, sw.ElapsedMilliseconds);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[PIPELINE:ObjRec] Schritt 1/4: FEHLER beim Download von {StorageKey}", request.StorageKey);
        return Results.Problem("Failed to download image for recognition");
    }

    logger.LogInformation("[PIPELINE:ObjRec] Schritt 2/4: YOLO-Face Erkennung...");
    List<DetectedFace> detectedFaces;
    try
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        detectedFaces = yolo.DetectFaces(imageBytes);
        sw.Stop();
        logger.LogInformation("[PIPELINE:ObjRec] Schritt 2/4: YOLO hat {FaceCount} Gesicht(er) erkannt in {ElapsedMs}ms",
            detectedFaces.Count, sw.ElapsedMilliseconds);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[PIPELINE:ObjRec] Schritt 2/4: YOLO FEHLGESCHLAGEN");
        detectedFaces = [];
    }

    var personCount = detectedFaces.Count;

    logger.LogInformation("[PIPELINE:ObjRec] Schritt 3/4: PersonCount={PersonCount} in MetadataStorage speichern...", personCount);
    await metadataStorage.UpdatePersonCountAsync(request.MediaId, personCount);

    var faces = new List<FaceInfo>();
    if (personCount > 0)
    {
        logger.LogInformation("[PIPELINE:ObjRec] Schritt 4/4: {PersonCount} Gesicht(er) mit Ollama beschreiben...", personCount);
        for (int i = 0; i < detectedFaces.Count; i++)
        {
            var detected = detectedFaces[i];
            logger.LogInformation(
                "[PIPELINE:ObjRec] Gesicht {Index}/{Total}: Bbox=({X:F1},{Y:F1},{W:F1},{H:F1}) Conf={Conf:F2}",
                i + 1, personCount, detected.BboxX, detected.BboxY, detected.BboxWidth, detected.BboxHeight, detected.Confidence);

            var description = await ollama.DescribeFaceAsync(base64Image, i, personCount);

            var faceInfo = new FaceInfo(
                description,
                detected.BboxX,
                detected.BboxY,
                detected.BboxWidth,
                detected.BboxHeight);

            faces.Add(faceInfo);
            await metadataStorage.StoreFaceAsync(request.MediaId, faceInfo.FaceDescription,
                faceInfo.BboxX, faceInfo.BboxY, faceInfo.BboxWidth, faceInfo.BboxHeight);
            logger.LogInformation("[PIPELINE:ObjRec] Gesicht {Index}/{Total}: Gespeichert", i + 1, personCount);
        }
    }
    else
    {
        logger.LogInformation("[PIPELINE:ObjRec] Schritt 4/4: Übersprungen (keine Gesichter erkannt)");
    }

    logger.LogInformation("[PIPELINE:ObjRec] ===== Recognition abgeschlossen: MediaId={MediaId}, Personen={PersonCount}, Gesichter={FaceCount} =====",
        request.MediaId, personCount, faces.Count);
    return Results.Ok(new RecognizeResponse(request.MediaId, personCount, faces));
});

app.Run();
