using LuminaVault.MediaImport;
using Microsoft.EntityFrameworkCore;
using Minio;
using Minio.DataModel.Args;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<MediaImportDbContext>("luminavault-metadata");
builder.AddMinioClient("minio");

builder.Services.AddHttpClient<ThumbnailServiceClient>(client =>
    client.BaseAddress = new Uri("http://thumbnail-generation"));

builder.Services.AddHttpClient<ObjectRecognitionServiceClient>(client =>
    client.BaseAddress = new Uri("http://object-recognition"));

var app = builder.Build();

app.MapDefaultEndpoints();

app.Logger.LogInformation("[PIPELINE] ===== MediaImport Service gestartet — Pipeline-Logging aktiv =====");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MediaImportDbContext>();
    db.Database.Migrate();
}

app.MapPost("/import", async (HttpRequest httpRequest, IMinioClient minio,
    MediaImportDbContext db, ThumbnailServiceClient thumbnails,
    ObjectRecognitionServiceClient recognition, ILogger<Program> logger) =>
{
    logger.LogInformation("[PIPELINE] ===== Import gestartet =====");

    if (!httpRequest.HasFormContentType)
    {
        logger.LogWarning("[PIPELINE] Abbruch: Kein Multipart-Form-Data");
        return Results.BadRequest("Multipart form data expected.");
    }

    var form = await httpRequest.ReadFormAsync();
    var file = form.Files.GetFile("file");
    if (file is null || file.Length == 0)
    {
        logger.LogWarning("[PIPELINE] Abbruch: Keine Datei im Upload");
        return Results.BadRequest("No file provided.");
    }

    var title = form["title"].ToString();
    if (string.IsNullOrWhiteSpace(title))
        title = Path.GetFileNameWithoutExtension(file.FileName);

    logger.LogInformation("[PIPELINE] Schritt 1/5: Datei empfangen - {FileName} ({ContentType}, {Size} bytes)",
        file.FileName, file.ContentType, file.Length);

    const string bucket = "media";
    await EnsureBucketExistsAsync(minio, bucket);

    var mediaId = Guid.NewGuid();
    var storageKey = $"{mediaId}/{file.FileName}";

    logger.LogInformation("[PIPELINE] Schritt 2/5: MinIO-Upload starten - MediaId={MediaId}, Bucket={Bucket}, Key={Key}",
        mediaId, bucket, storageKey);

    var sw = System.Diagnostics.Stopwatch.StartNew();
    await using var stream = file.OpenReadStream();
    await minio.PutObjectAsync(new PutObjectArgs()
        .WithBucket(bucket)
        .WithObject(storageKey)
        .WithStreamData(stream)
        .WithObjectSize(file.Length)
        .WithContentType(file.ContentType));
    sw.Stop();

    logger.LogInformation("[PIPELINE] Schritt 2/5: MinIO-Upload abgeschlossen in {ElapsedMs}ms", sw.ElapsedMilliseconds);

    var mediaItem = new MediaItem
    {
        Id = mediaId,
        FileName = file.FileName,
        ContentType = file.ContentType,
        FileSizeBytes = file.Length,
        ImportedAt = DateTimeOffset.UtcNow,
        StorageBucket = bucket,
        StorageKey = storageKey
    };

    logger.LogInformation("[PIPELINE] Schritt 3/5: MediaItem in DB speichern - MediaId={MediaId}", mediaId);
    await db.MediaItems.AddAsync(mediaItem);
    await db.SaveChangesAsync();
    logger.LogInformation("[PIPELINE] Schritt 3/5: DB-Speicherung abgeschlossen");

    // Generate thumbnail
    if (file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
    {
        logger.LogInformation("[PIPELINE] Schritt 4/5: Thumbnail-Generierung starten - MediaId={MediaId}", mediaId);
        try
        {
            sw.Restart();
            await thumbnails.RequestThumbnailAsync(mediaId, bucket, storageKey);
            sw.Stop();
            logger.LogInformation("[PIPELINE] Schritt 4/5: Thumbnail-Generierung abgeschlossen in {ElapsedMs}ms", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[PIPELINE] Schritt 4/5: Thumbnail-Generierung FEHLGESCHLAGEN für MediaId={MediaId}", mediaId);
        }

        logger.LogInformation("[PIPELINE] Schritt 5/5: ObjectRecognition starten - MediaId={MediaId}", mediaId);
        try
        {
            sw.Restart();
            await recognition.RecognizeAsync(mediaId, file.ContentType, bucket, storageKey);
            sw.Stop();
            logger.LogInformation("[PIPELINE] Schritt 5/5: ObjectRecognition abgeschlossen in {ElapsedMs}ms", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[PIPELINE] Schritt 5/5: ObjectRecognition FEHLGESCHLAGEN für MediaId={MediaId}", mediaId);
        }
    }
    else
    {
        logger.LogInformation("[PIPELINE] Schritt 4/5: Thumbnail übersprungen (kein Bild)");
        logger.LogInformation("[PIPELINE] Schritt 5/5: ObjectRecognition übersprungen (kein Bild)");
    }

    logger.LogInformation("[PIPELINE] ===== Import abgeschlossen: MediaId={MediaId}, Datei={FileName} =====", mediaId, file.FileName);
    return Results.Created($"/media/{mediaId}", mediaItem);
}).DisableAntiforgery();

app.MapGet("/media/{id:guid}", async (Guid id, MediaImportDbContext db) =>
    await db.MediaItems.FindAsync(id) is MediaItem item
        ? Results.Ok(item)
        : Results.NotFound());

app.MapGet("/media", async (MediaImportDbContext db) =>
    Results.Ok(await db.MediaItems.OrderByDescending(m => m.ImportedAt).ToListAsync()));

app.MapGet("/media/{id:guid}/url", async (Guid id, MediaImportDbContext db, IMinioClient minio) =>
{
    var item = await db.MediaItems.FindAsync(id);
    if (item is null) return Results.NotFound();

    var presignedUrl = await minio.PresignedGetObjectAsync(new PresignedGetObjectArgs()
        .WithBucket(item.StorageBucket)
        .WithObject(item.StorageKey)
        .WithExpiry(3600));
    return Results.Ok(new { Url = presignedUrl });
});

app.Run();

static async Task EnsureBucketExistsAsync(IMinioClient minio, string bucket)
{
    bool exists = await minio.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucket));
    if (!exists)
        await minio.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucket));
}
