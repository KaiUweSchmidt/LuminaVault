using LuminaVault.MediaImport;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Minio;
using Minio.DataModel.Args;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddDbContext<MediaImportDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("luminavault-metadata")));

builder.Services.AddSingleton<IMinioClient>(sp =>
{
    var config = builder.Configuration.GetSection("Minio");
    return new MinioClient()
        .WithEndpoint(config["Endpoint"])
        .WithCredentials(config["AccessKey"], config["SecretKey"])
        .Build();
});

builder.Services.AddHttpClient<ThumbnailServiceClient>(client =>
    client.BaseAddress = new Uri(builder.Configuration["Services:ThumbnailGeneration"]
        ?? "http://thumbnail-generation:8080"));

builder.Services.AddHttpClient<ObjectRecognitionServiceClient>(client =>
    client.BaseAddress = new Uri(builder.Configuration["Services:ObjectRecognition"]
        ?? "http://object-recognition:8080"));

builder.Services.AddHttpClient<MetadataStorageClient>(client =>
    client.BaseAddress = new Uri(builder.Configuration["Services:MetadataStorage"]
        ?? "http://metadata-storage:8080"));

builder.Services.AddHttpClient<IGeocodingService, NominatimGeocodingService>(client =>
{
    // Nominatim requires a meaningful User-Agent header per their usage policy
    client.DefaultRequestHeaders.UserAgent.ParseAdd("LuminaVault/1.0 (https://github.com/KaiUweSchmidt/LuminaVault)");
});

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
    ObjectRecognitionServiceClient recognition, MetadataStorageClient metadataStorage,
    IGeocodingService geocoding, ILogger<Program> logger) =>
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

    // Extract GPS coordinates and location name from EXIF (images only)
    double? gpsLatitude = null;
    double? gpsLongitude = null;
    string? gpsLocation = null;

    if (file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
    {
        try
        {
            await using var exifStream = file.OpenReadStream();
            var gpsCoords = GpsExifExtractor.ExtractGps(exifStream);
            if (gpsCoords.HasValue)
            {
                gpsLatitude = gpsCoords.Value.Latitude;
                gpsLongitude = gpsCoords.Value.Longitude;
                logger.LogInformation("[PIPELINE] GPS-Koordinaten extrahiert: ({Lat}, {Lon}) für MediaId={MediaId}",
                    gpsLatitude, gpsLongitude, mediaId);

                gpsLocation = await geocoding.GetLocationNameAsync(gpsCoords.Value.Latitude, gpsCoords.Value.Longitude);
                if (gpsLocation is not null)
                    logger.LogInformation("[PIPELINE] GPS-Ort ermittelt: '{Location}' für MediaId={MediaId}", gpsLocation, mediaId);
                else
                    logger.LogInformation("[PIPELINE] GPS-Ort konnte nicht ermittelt werden für MediaId={MediaId}", mediaId);
            }
            else
            {
                logger.LogInformation("[PIPELINE] Keine GPS-Daten in EXIF gefunden für MediaId={MediaId}", mediaId);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[PIPELINE] GPS-Extraktion fehlgeschlagen für MediaId={MediaId}", mediaId);
        }
    }

    // Create metadata entry in MetadataStorage service
    try
    {
        await metadataStorage.CreateMetadataAsync(mediaId, title, file.FileName, file.ContentType,
            file.Length, gpsLatitude, gpsLongitude, gpsLocation);
        logger.LogInformation("[PIPELINE] MetadataStorage-Eintrag erstellt für MediaId={MediaId}", mediaId);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "[PIPELINE] MetadataStorage-Eintrag FEHLGESCHLAGEN für MediaId={MediaId}", mediaId);
    }

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

    var ms = new MemoryStream();
    await minio.GetObjectAsync(new GetObjectArgs()
        .WithBucket(item.StorageBucket)
        .WithObject(item.StorageKey)
        .WithCallbackStream(stream => stream.CopyTo(ms)));
    ms.Position = 0;

    return Results.File(ms, item.ContentType, item.FileName);
});

app.MapDelete("/admin/purge-all", async (MediaImportDbContext db, IMinioClient minio, ILogger<Program> logger) =>
{
    var deletedItems = await db.MediaItems.ExecuteDeleteAsync();
    logger.LogWarning("[ADMIN] Purge-All: {Count} MediaItems aus DB gelöscht", deletedItems);

    var deletedObjects = 0;
    string[] buckets = ["media", "thumbnails"];
    foreach (var bucket in buckets)
    {
        var exists = await minio.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucket));
        if (!exists) continue;

        var objects = new List<string>();
        var listArgs = new ListObjectsArgs().WithBucket(bucket).WithRecursive(true);
        await foreach (var item in minio.ListObjectsEnumAsync(listArgs))
        {
            objects.Add(item.Key);
        }

        if (objects.Count > 0)
        {
            await minio.RemoveObjectsAsync(new RemoveObjectsArgs()
                .WithBucket(bucket)
                .WithObjects(objects));
            deletedObjects += objects.Count;
        }

        logger.LogWarning("[ADMIN] Purge-All: {Count} Objekte aus Bucket '{Bucket}' gelöscht", objects.Count, bucket);
    }

    return Results.Ok(new { DeletedMediaItems = deletedItems, DeletedObjects = deletedObjects });
});

app.Run();

static async Task EnsureBucketExistsAsync(IMinioClient minio, string bucket)
{
    bool exists = await minio.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucket));
    if (!exists)
        await minio.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucket));
}

public partial class Program { }
