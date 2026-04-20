using LuminaVault.MetadataStorage;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<MetadataDbContext>("luminavault-metadata");

var app = builder.Build();

app.MapDefaultEndpoints();

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

app.MapPost("/media", async (CreateMediaMetadataRequest request, MetadataDbContext db) =>
{
    var metadata = new MediaMetadata
    {
        Id = request.MediaId,
        Title = request.Title,
        Description = request.Description,
        Tags = request.Tags,
        GpsLatitude = request.GpsLatitude,
        GpsLongitude = request.GpsLongitude,
        CreatedAt = DateTimeOffset.UtcNow
    };
    await db.MediaMetadata.AddAsync(metadata);
    await db.SaveChangesAsync();
    return Results.Created($"/media/{metadata.Id}", metadata);
});

app.MapPut("/media/{id:guid}", async (Guid id, UpdateMediaMetadataRequest request, MetadataDbContext db) =>
{
    var metadata = await db.MediaMetadata.FindAsync(id);
    if (metadata is null) return Results.NotFound();

    metadata.Title = request.Title ?? metadata.Title;
    metadata.Description = request.Description ?? metadata.Description;
    metadata.Tags = request.Tags ?? metadata.Tags;
    if (request.PersonCount.HasValue)
        metadata.PersonCount = request.PersonCount;
    metadata.UpdatedAt = DateTimeOffset.UtcNow;

    await db.SaveChangesAsync();
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

app.MapPost("/faces", async (CreateFaceRequest request, MetadataDbContext db) =>
{
    var face = new Face
    {
        Id = Guid.NewGuid(),
        MediaId = request.MediaId,
        FaceDescription = request.FaceDescription,
        Name = request.Name,
        DetectedAt = DateTimeOffset.UtcNow
    };
    await db.Faces.AddAsync(face);
    await db.SaveChangesAsync();
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

app.Run();
