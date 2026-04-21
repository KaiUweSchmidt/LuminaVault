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

builder.Services.AddHttpClient<FaceRecognitionClient>(client =>
    client.BaseAddress = new Uri(builder.Configuration["Services:FaceRecognition"]
        ?? "http://face-recognition:8080"));

builder.Services.AddHttpClient<AiTaggingClient>(client =>
    client.BaseAddress = new Uri(builder.Configuration["Services:AiTagging"]
        ?? "http://ai-tagging:8080"));

var app = builder.Build();

app.MapDefaultEndpoints();

app.Logger.LogInformation("[PIPELINE:ObjRec] ===== ObjectRecognition Service gestartet — Ollama Objekterkennung =====");

app.MapPost("/recognize", async (
    RecognizeRequest request,
    IMinioClient minio,
    OllamaClient ollama,
    FaceRecognitionClient faceRecognition,
    AiTaggingClient aiTagging,
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

    string base64Image;
    try
    {
        logger.LogInformation("[PIPELINE:ObjRec] Schritt 1/3: Bild aus MinIO herunterladen...");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var imageStream = new MemoryStream();
        await minio.GetObjectAsync(new GetObjectArgs()
            .WithBucket(request.StorageBucket)
            .WithObject(request.StorageKey)
            .WithCallbackStream(stream => stream.CopyTo(imageStream)));
        var imageBytes = imageStream.ToArray();
        base64Image = Convert.ToBase64String(imageBytes);
        sw.Stop();
        logger.LogInformation("[PIPELINE:ObjRec] Schritt 1/3: Bild heruntergeladen ({SizeKb}KB) in {ElapsedMs}ms",
            imageBytes.Length / 1024, sw.ElapsedMilliseconds);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[PIPELINE:ObjRec] Schritt 1/3: FEHLER beim Download von {StorageKey}", request.StorageKey);
        return Results.Problem("Failed to download image for object recognition");
    }

    logger.LogInformation("[PIPELINE:ObjRec] Schritt 2/3: Ollama Objekterkennung...");
    OllamaDetectionResult detection;
    try
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        detection = await ollama.DetectObjectsAsync(base64Image);
        sw.Stop();
        logger.LogInformation("[PIPELINE:ObjRec] Schritt 2/3: Ollama hat [{Objects}] erkannt in {ElapsedMs}ms (PersonDetected={PersonDetected})",
            string.Join(", ", detection.Objects ?? []), sw.ElapsedMilliseconds, detection.PersonDetected);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[PIPELINE:ObjRec] Schritt 2/3: Ollama Objekterkennung FEHLGESCHLAGEN");
        detection = new OllamaDetectionResult([], false);
    }

    var detectedObjects = detection.Objects ?? [];
    var personDetected = detection.PersonDetected ?? false;

    logger.LogInformation("[PIPELINE:ObjRec] Schritt 3/3: Ergebnisse weiterleiten...");

    if (personDetected)
    {
        logger.LogInformation("[PIPELINE:ObjRec] Person erkannt → FaceRecognition aufrufen für MediaId={MediaId}", request.MediaId);
        await faceRecognition.RecognizeFacesAsync(request.MediaId, request.StorageBucket, request.StorageKey);
    }

    var nonPersonObjects = detectedObjects
        .Where(o => !string.Equals(o, "person", StringComparison.OrdinalIgnoreCase))
        .ToList();

    if (nonPersonObjects.Count > 0)
    {
        logger.LogInformation("[PIPELINE:ObjRec] Nicht-Personen-Objekte [{Objects}] → AiTagging für MediaId={MediaId}",
            string.Join(", ", nonPersonObjects), request.MediaId);
        await aiTagging.StoreTagsAsync(request.MediaId, nonPersonObjects);
    }

    logger.LogInformation("[PIPELINE:ObjRec] ===== Objekterkennung abgeschlossen: MediaId={MediaId}, PersonDetected={PersonDetected}, Objekte=[{Objects}] =====",
        request.MediaId, personDetected, string.Join(", ", detectedObjects));
    return Results.Ok(new RecognizeResponse(request.MediaId, personDetected, detectedObjects));
});

app.Run();
