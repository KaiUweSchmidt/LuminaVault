using LuminaVault.MediaImport;
using Microsoft.EntityFrameworkCore;
using Minio;

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
    var mediaItem = new MediaItem
    {
        Id = Guid.NewGuid(),
        FileName = request.FileName,
        ContentType = request.ContentType,
        FileSizeBytes = request.FileSizeBytes,
        ImportedAt = DateTimeOffset.UtcNow,
        StorageBucket = "media",
        StorageKey = $"{Guid.NewGuid()}/{request.FileName}"
    };

    await db.MediaItems.AddAsync(mediaItem);
    await db.SaveChangesAsync();

    await thumbnails.RequestThumbnailAsync(mediaItem.Id, mediaItem.StorageBucket, mediaItem.StorageKey);

    _ = Task.Run(async () =>
    {
        try
        {
            await recognition.RecognizeAsync(mediaItem.Id, mediaItem.ContentType, mediaItem.StorageBucket, mediaItem.StorageKey);
        }
        catch (Exception ex)
        {
            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "Background object recognition failed for media {MediaId}", mediaItem.Id);
        }
    });

    return Results.Created($"/media/{mediaItem.Id}", mediaItem);
});

app.MapGet("/media/{id:guid}", async (Guid id, MediaImportDbContext db) =>
    await db.MediaItems.FindAsync(id) is MediaItem item
        ? Results.Ok(item)
        : Results.NotFound());

app.MapGet("/media", async (MediaImportDbContext db) =>
    Results.Ok(await db.MediaItems.OrderByDescending(m => m.ImportedAt).ToListAsync()));

app.Run();
