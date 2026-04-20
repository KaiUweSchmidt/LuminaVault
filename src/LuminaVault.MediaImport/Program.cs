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

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MediaImportDbContext>();
    db.Database.Migrate();
}

app.MapPost("/import", async (ImportMediaRequest request, IMinioClient minio,
    MediaImportDbContext db, ThumbnailServiceClient thumbnails,
    ObjectRecognitionServiceClient recognition) =>
{
    if (!httpRequest.HasFormContentType)
        return Results.BadRequest("Multipart form data expected.");

    var form = await httpRequest.ReadFormAsync();
    var file = form.Files.GetFile("file");
    if (file is null || file.Length == 0)
        return Results.BadRequest("No file provided.");

    var title = form["title"].ToString();
    if (string.IsNullOrWhiteSpace(title))
        title = Path.GetFileNameWithoutExtension(file.FileName);

    const string bucket = "media";
    await EnsureBucketExistsAsync(minio, bucket);

    var mediaId = Guid.NewGuid();
    var storageKey = $"{mediaId}/{file.FileName}";

    await using var stream = file.OpenReadStream();
    await minio.PutObjectAsync(new PutObjectArgs()
        .WithBucket(bucket)
        .WithObject(storageKey)
        .WithStreamData(stream)
        .WithObjectSize(file.Length)
        .WithContentType(file.ContentType));

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

    await db.MediaItems.AddAsync(mediaItem);
    await db.SaveChangesAsync();

    // Store metadata
    try
    {
        await metadataClient.CreateMetadataAsync(mediaId, title, file.FileName, file.ContentType);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to create metadata for media {MediaId}", mediaId);
    }

    // Generate thumbnail (fire-and-forget with logging)
    if (file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
    {
        try
        {
            await thumbnails.RequestThumbnailAsync(mediaId, bucket, storageKey);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to generate thumbnail for media {MediaId}", mediaId);
        }
    }

    logger.LogInformation("Media imported: {MediaId} ({FileName})", mediaId, file.FileName);
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
