using LuminaVault.MetadataStorage;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddDbContext<MetadataDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("luminavault-metadata")));

var app = builder.Build();

app.MapDefaultEndpoints();

app.Logger.LogInformation("[PIPELINE:MetaStore] ===== MetadataStorage Service gestartet — Pipeline-Logging aktiv =====");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MetadataDbContext>();
    db.Database.Migrate();
}

app.MapGet("/media", async (MetadataDbContext db, string? tag, string? search) =>
{
    var query = db.MediaMetadata.AsQueryable();
    if (!string.IsNullOrWhiteSpace(tag))
        query = query.Where(m => m.Tags.Contains(tag));
    if (!string.IsNullOrWhiteSpace(search))
        query = query.Where(m => m.Title.Contains(search) || m.Description.Contains(search));
    return Results.Ok(await query.OrderByDescending(m => m.CreatedAt).ToListAsync());
});

app.MapGet("/media/{id:guid}", async (Guid id, MetadataDbContext db) =>
    await db.MediaMetadata.FindAsync(id) is MediaMetadata metadata
        ? Results.Ok(metadata)
        : Results.NotFound());

app.MapPost("/media", async (CreateMediaMetadataRequest request, MetadataDbContext db, ILogger<Program> logger) =>
{
    logger.LogInformation("[PIPELINE:MetaStore] POST /media - Metadata anlegen für MediaId={MediaId}, Title={Title}",
        request.MediaId, request.Title);
    var metadata = new MediaMetadata
    {
        Id = request.MediaId,
        FileName = request.FileName,
        ContentType = request.ContentType,
        FileSizeBytes = request.FileSizeBytes,
        Title = request.Title,
        Description = request.Description,
        Tags = request.Tags,
        GpsLatitude = request.GpsLatitude,
        GpsLongitude = request.GpsLongitude,
        GpsLocation = request.GpsLocation,
        CreatedAt = request.CapturedAt ?? DateTimeOffset.UtcNow
    };
    await db.MediaMetadata.AddAsync(metadata);
    await db.SaveChangesAsync();
    logger.LogInformation("[PIPELINE:MetaStore] POST /media - Metadata gespeichert für MediaId={MediaId}", request.MediaId);
    return Results.Created($"/media/{metadata.Id}", metadata);
});

app.MapPut("/media/{id:guid}", async (Guid id, UpdateMediaMetadataRequest request, MetadataDbContext db, ILogger<Program> logger) =>
{
    logger.LogInformation("[PIPELINE:MetaStore] PUT /media/{MediaId} - Update angefordert (PersonCount={PersonCount}, Title={Title})",
        id, request.PersonCount, request.Title);
    var metadata = await db.MediaMetadata.FindAsync(id);
    if (metadata is null)
    {
        logger.LogWarning("[PIPELINE:MetaStore] PUT /media/{MediaId} - Metadata NICHT GEFUNDEN", id);
        return Results.NotFound();
    }

    metadata.Title = request.Title ?? metadata.Title;
    metadata.Description = request.Description ?? metadata.Description;
    metadata.Tags = request.Tags ?? metadata.Tags;
    if (request.PersonCount.HasValue)
        metadata.PersonCount = request.PersonCount;
    metadata.UpdatedAt = DateTimeOffset.UtcNow;

    await db.SaveChangesAsync();
    logger.LogInformation("[PIPELINE:MetaStore] PUT /media/{MediaId} - Update gespeichert (PersonCount={PersonCount})",
        id, metadata.PersonCount);
    return Results.Ok(metadata);
});

app.MapDelete("/media/{id:guid}", async (Guid id, MetadataDbContext db) =>
{
    var metadata = await db.MediaMetadata.FindAsync(id);
    if (metadata is null) return Results.NotFound();
    db.MediaMetadata.Remove(metadata);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// Face endpoints
app.MapGet("/faces/{mediaId:guid}", async (Guid mediaId, MetadataDbContext db) =>
{
    var faces = await db.Faces
        .Where(f => f.MediaId == mediaId)
        .OrderBy(f => f.DetectedAt)
        .ToListAsync();
    return Results.Ok(faces);
});

app.MapPost("/faces", async (CreateFaceRequest request, MetadataDbContext db, ILogger<Program> logger) =>
{
    logger.LogInformation("[PIPELINE:MetaStore] POST /faces - Gesicht speichern für MediaId={MediaId}, Beschreibung={DescLen} Zeichen",
        request.MediaId, request.FaceDescription?.Length ?? 0);
    var face = new Face
    {
        Id = Guid.NewGuid(),
        MediaId = request.MediaId,
        FaceDescription = request.FaceDescription ?? string.Empty,
        Name = request.Name,
        BboxX = request.BboxX,
        BboxY = request.BboxY,
        BboxWidth = request.BboxWidth,
        BboxHeight = request.BboxHeight,
        DetectedAt = DateTimeOffset.UtcNow
    };
    await db.Faces.AddAsync(face);
    await db.SaveChangesAsync();
    logger.LogInformation("[PIPELINE:MetaStore] POST /faces - Gesicht gespeichert: FaceId={FaceId} für MediaId={MediaId}",
        face.Id, request.MediaId);
    return Results.Created($"/faces/{face.MediaId}/{face.Id}", face);
});

app.MapPut("/faces/{id:guid}", async (Guid id, UpdateFaceNameRequest request, MetadataDbContext db) =>
{
    var face = await db.Faces.FindAsync(id);
    if (face is null) return Results.NotFound();
    face.Name = request.Name;
    await db.SaveChangesAsync();
    return Results.Ok(face);
});

app.MapDelete("/faces/{id:guid}", async (Guid id, MetadataDbContext db) =>
{
    var face = await db.Faces.FindAsync(id);
    if (face is null) return Results.NotFound();
    db.Faces.Remove(face);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// Find media items with persons similar to the given media item
app.MapGet("/faces/similar/{mediaId:guid}", async (Guid mediaId, MetadataDbContext db) =>
{
    var results = await db.MediaMetadata
        .Where(m => m.PersonCount > 0 && m.Id != mediaId)
        .OrderByDescending(m => m.PersonCount)
        .ToListAsync();
    return Results.Ok(results);
});

app.MapGet("/faces/names", async (MetadataDbContext db) =>
{
    var names = await db.Faces
        .Where(f => !string.IsNullOrEmpty(f.Name))
        .Select(f => f.Name!)
        .Distinct()
        .OrderBy(n => n)
        .ToListAsync();
    return Results.Ok(names);
});

app.MapDelete("/admin/purge-all", async (MetadataDbContext db, ILogger<Program> logger) =>
{
    var faceCount = await db.Faces.ExecuteDeleteAsync();
    var metaCount = await db.MediaMetadata.ExecuteDeleteAsync();
    logger.LogWarning("[ADMIN] Purge-All: {FaceCount} Faces und {MetaCount} Metadata gelöscht", faceCount, metaCount);
    return Results.Ok(new { DeletedFaces = faceCount, DeletedMetadata = metaCount });
});

// Collections endpoints
app.MapGet("/collections", async (MetadataDbContext db) =>
{
    var collections = await db.Collections
        .Select(c => new
        {
            c.Id,
            c.Name,
            c.Description,
            c.CreatedAt,
            ImageCount = db.CollectionMediaItems.Count(i => i.CollectionId == c.Id)
        })
        .OrderBy(c => c.Name)
        .ToListAsync();
    return Results.Ok(collections);
});

app.MapGet("/collections/{id:guid}", async (Guid id, MetadataDbContext db) =>
{
    var collection = await db.Collections.FindAsync(id);
    return collection is null ? Results.NotFound() : Results.Ok(collection);
});

app.MapPost("/collections", async (CreateCollectionRequest request, MetadataDbContext db) =>
{
    var collection = new Collection
    {
        Id = Guid.NewGuid(),
        Name = request.Name,
        Description = request.Description ?? string.Empty,
        CreatedAt = DateTimeOffset.UtcNow
    };
    await db.Collections.AddAsync(collection);
    await db.SaveChangesAsync();
    return Results.Created($"/collections/{collection.Id}", collection);
});

app.MapDelete("/collections/{id:guid}", async (Guid id, MetadataDbContext db) =>
{
    var collection = await db.Collections.FindAsync(id);
    if (collection is null) return Results.NotFound();
    db.Collections.Remove(collection);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

app.MapPost("/collections/{id:guid}/media/{mediaId:guid}", async (Guid id, Guid mediaId, MetadataDbContext db) =>
{
    var collection = await db.Collections.FindAsync(id);
    if (collection is null) return Results.NotFound();
    var exists = await db.CollectionMediaItems.AnyAsync(i => i.CollectionId == id && i.MediaId == mediaId);
    if (!exists)
    {
        await db.CollectionMediaItems.AddAsync(new CollectionMediaItem { CollectionId = id, MediaId = mediaId });
        await db.SaveChangesAsync();
    }
    return Results.Ok();
});

app.MapDelete("/collections/{id:guid}/media/{mediaId:guid}", async (Guid id, Guid mediaId, MetadataDbContext db) =>
{
    var item = await db.CollectionMediaItems.FindAsync(id, mediaId);
    if (item is null) return Results.NotFound();
    db.CollectionMediaItems.Remove(item);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

app.MapGet("/collections/{id:guid}/media", async (Guid id, MetadataDbContext db) =>
{
    var mediaIds = await db.CollectionMediaItems
        .Where(i => i.CollectionId == id)
        .Select(i => i.MediaId)
        .ToListAsync();
    return Results.Ok(mediaIds);
});

app.Run();
