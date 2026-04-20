using LuminaVault.ObjectRecognition;
using Minio;
using Minio.DataModel.Args;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddMinioClient("minio");

builder.Services.AddHttpClient<OllamaClient>(client =>
    client.BaseAddress = new Uri(builder.Configuration["Ollama:Endpoint"] ?? "http://ollama:11434"));

builder.Services.AddHttpClient<MetadataStorageClient>(client =>
    client.BaseAddress = new Uri("http://metadata-storage"));

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapPost("/recognize", async (
    RecognizeRequest request,
    IMinioClient minio,
    OllamaClient ollama,
    MetadataStorageClient metadataStorage,
    ILogger<Program> logger) =>
{
    if (!request.ContentType.StartsWith("image/"))
    {
        logger.LogInformation("Skipping recognition for non-image content type {ContentType}", request.ContentType);
        return Results.Ok(new RecognizeResponse(request.MediaId, 0, []));
    }

    string base64Image;
    try
    {
        var imageStream = new MemoryStream();
        await minio.GetObjectAsync(new GetObjectArgs()
            .WithBucket(request.StorageBucket)
            .WithObject(request.StorageKey)
            .WithCallbackStream(stream => stream.CopyTo(imageStream)));
        imageStream.Position = 0;
        base64Image = Convert.ToBase64String(imageStream.ToArray());
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to download image {StorageKey} for recognition", request.StorageKey);
        return Results.Problem("Failed to download image for recognition");
    }

    var personCount = await ollama.CountPersonsAsync(base64Image);
    logger.LogInformation("Detected {PersonCount} person(s) in media {MediaId}", personCount, request.MediaId);

    await metadataStorage.UpdatePersonCountAsync(request.MediaId, personCount);

    var faces = new List<FaceInfo>();
    if (personCount > 0)
    {
        for (int i = 0; i < personCount; i++)
        {
            var description = await ollama.DescribeFaceAsync(base64Image, i);
            if (!string.IsNullOrWhiteSpace(description))
            {
                faces.Add(new FaceInfo(description));
                await metadataStorage.StoreFaceAsync(request.MediaId, description);
                logger.LogInformation("Stored face {Index} for media {MediaId}", i, request.MediaId);
            }
        }
    }

    return Results.Ok(new RecognizeResponse(request.MediaId, personCount, faces));
});

app.Run();
