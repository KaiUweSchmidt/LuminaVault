using LuminaVault.ObjectRecognition;
using Minio;
using Minio.DataModel.Args;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddMinioClient("minio");

builder.Services.AddHttpClient<OllamaClient>((sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var connectionString = config.GetConnectionString("ollama");
    var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("OllamaSetup");
    logger.LogInformation("[PIPELINE:ObjRec] Ollama ConnectionString={ConnectionString}", connectionString ?? "(null)");
    client.BaseAddress = new Uri(connectionString ?? "http://localhost:11434");
});

builder.Services.AddHttpClient<MetadataStorageClient>(client =>
    client.BaseAddress = new Uri("http://metadata-storage"));

var app = builder.Build();

app.MapDefaultEndpoints();

app.Logger.LogInformation("[PIPELINE:ObjRec] ===== ObjectRecognition Service gestartet — Pipeline-Logging aktiv =====");

app.MapPost("/recognize", async (
    RecognizeRequest request,
    IMinioClient minio,
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
        imageStream.Position = 0;
        base64Image = Convert.ToBase64String(imageStream.ToArray());
        sw.Stop();
        logger.LogInformation("[PIPELINE:ObjRec] Schritt 1/4: Bild heruntergeladen ({SizeKb}KB, Base64={Base64Len} Zeichen) in {ElapsedMs}ms",
            imageStream.Length / 1024, base64Image.Length, sw.ElapsedMilliseconds);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[PIPELINE:ObjRec] Schritt 1/4: FEHLER beim Download von {StorageKey}", request.StorageKey);
        return Results.Problem("Failed to download image for recognition");
    }

    logger.LogInformation("[PIPELINE:ObjRec] Schritt 2/4: Ollama CountPersons aufrufen...");
    var swOllama = System.Diagnostics.Stopwatch.StartNew();
    var personCount = await ollama.CountPersonsAsync(base64Image);
    swOllama.Stop();
    logger.LogInformation("[PIPELINE:ObjRec] Schritt 2/4: Ollama hat {PersonCount} Person(en) erkannt in {ElapsedMs}ms",
        personCount, swOllama.ElapsedMilliseconds);

    logger.LogInformation("[PIPELINE:ObjRec] Schritt 3/4: PersonCount in MetadataStorage speichern...");
    await metadataStorage.UpdatePersonCountAsync(request.MediaId, personCount);
    logger.LogInformation("[PIPELINE:ObjRec] Schritt 3/4: PersonCount gespeichert");

    var faces = new List<FaceInfo>();
    if (personCount > 0)
    {
        logger.LogInformation("[PIPELINE:ObjRec] Schritt 4/4: {PersonCount} Gesicht(er) beschreiben und speichern...", personCount);
        for (int i = 0; i < personCount; i++)
        {
            logger.LogInformation("[PIPELINE:ObjRec] Gesicht {Index}/{Total}: Ollama DescribeFace aufrufen...", i + 1, personCount);
            swOllama.Restart();
            var description = await ollama.DescribeFaceAsync(base64Image, i);
            swOllama.Stop();
            if (!string.IsNullOrWhiteSpace(description))
            {
                logger.LogInformation("[PIPELINE:ObjRec] Gesicht {Index}/{Total}: Beschreibung erhalten ({DescLen} Zeichen) in {ElapsedMs}ms",
                    i + 1, personCount, description.Length, swOllama.ElapsedMilliseconds);
                faces.Add(new FaceInfo(description));
                await metadataStorage.StoreFaceAsync(request.MediaId, description);
                logger.LogInformation("[PIPELINE:ObjRec] Gesicht {Index}/{Total}: In MetadataStorage gespeichert", i + 1, personCount);
            }
            else
            {
                logger.LogWarning("[PIPELINE:ObjRec] Gesicht {Index}/{Total}: Leere Beschreibung von Ollama in {ElapsedMs}ms",
                    i + 1, personCount, swOllama.ElapsedMilliseconds);
            }
        }
    }
    else
    {
        logger.LogInformation("[PIPELINE:ObjRec] Schritt 4/4: Übersprungen (keine Personen erkannt)");
    }

    logger.LogInformation("[PIPELINE:ObjRec] ===== Recognition abgeschlossen: MediaId={MediaId}, Personen={PersonCount}, Gesichter={FaceCount} =====",
        request.MediaId, personCount, faces.Count);
    return Results.Ok(new RecognizeResponse(request.MediaId, personCount, faces));
});

app.Run();
