using LuminaVault.AiTagging;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddDbContext<AiTaggingDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("luminavault-metadata")));

var app = builder.Build();

app.MapDefaultEndpoints();

app.Logger.LogInformation("[PIPELINE:AiTag] ===== AiTagging Service gestartet — Pipeline-Logging aktiv =====");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AiTaggingDbContext>();
    db.Database.Migrate();
}

app.MapPost("/analyze", async (AnalyzeRequest request, AiTaggingDbContext db, ILogger<Program> logger) =>
{
    logger.LogInformation("[PIPELINE:AiTag] POST /analyze - Analyse starten für MediaId={MediaId}, ContentType={ContentType}",
        request.MediaId, request.ContentType);
    var tags = GenerateTags(request.ContentType);
    logger.LogInformation("[PIPELINE:AiTag] Tags generiert: [{Tags}] für MediaId={MediaId}",
        string.Join(", ", tags), request.MediaId);
    var result = new TaggingResult
    {
        Id = Guid.NewGuid(),
        MediaId = request.MediaId,
        Tags = tags,
        Confidence = 0.92f,
        AnalyzedAt = DateTimeOffset.UtcNow
    };
    await db.TaggingResults.AddAsync(result);
    await db.SaveChangesAsync();
    logger.LogInformation("[PIPELINE:AiTag] POST /analyze - Ergebnis gespeichert: TaggingId={TaggingId} für MediaId={MediaId}",
        result.Id, request.MediaId);
    return Results.Ok(result);
});

app.MapGet("/tags/{mediaId:guid}", async (Guid mediaId, AiTaggingDbContext db) =>
{
    var result = await db.TaggingResults
        .Where(t => t.MediaId == mediaId)
        .OrderByDescending(t => t.AnalyzedAt)
        .FirstOrDefaultAsync();
    return result is not null ? Results.Ok(result) : Results.NotFound();
});

app.MapGet("/tags/popular", async (AiTaggingDbContext db, int top = 10) =>
{
    var popularTags = await db.TaggingResults
        .SelectMany(t => t.Tags)
        .GroupBy(tag => tag)
        .OrderByDescending(g => g.Count())
        .Take(top)
        .Select(g => new { Tag = g.Key, Count = g.Count() })
        .ToListAsync();
    return Results.Ok(popularTags);
});

app.Run();

static string[] GenerateTags(string contentType) =>
    contentType.StartsWith("image/") ? ["photo", "image", "visual"] :
    contentType.StartsWith("video/") ? ["video", "clip", "footage"] :
    ["media", "file"];
