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

app.Run();
