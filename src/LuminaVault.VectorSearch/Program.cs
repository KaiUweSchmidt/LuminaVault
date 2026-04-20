using LuminaVault.VectorSearch;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddDbContext<VectorSearchDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("luminavault-vectors"),
        o => o.UseVector()));

var app = builder.Build();

app.MapDefaultEndpoints();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<VectorSearchDbContext>();
    db.Database.Migrate();
}

app.MapPost("/embeddings", async (StoreEmbeddingRequest request, VectorSearchDbContext db) =>
{
    var existing = await db.MediaEmbeddings.FirstOrDefaultAsync(e => e.MediaId == request.MediaId);
    if (existing is not null)
    {
        existing.Embedding = new Vector(request.Embedding);
        existing.UpdatedAt = DateTimeOffset.UtcNow;
    }
    else
    {
        var embedding = new MediaEmbedding
        {
            Id = Guid.NewGuid(),
            MediaId = request.MediaId,
            Embedding = new Vector(request.Embedding),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await db.MediaEmbeddings.AddAsync(embedding);
    }
    await db.SaveChangesAsync();
    return Results.Ok();
});

app.MapPost("/search", async (SearchRequest request, VectorSearchDbContext db) =>
{
    var queryVector = new Vector(request.Embedding);
    var results = await db.MediaEmbeddings
        .OrderBy(e => e.Embedding.L2Distance(queryVector))
        .Take(request.TopK)
        .Select(e => new SearchResult(e.MediaId, e.Embedding.L2Distance(queryVector)))
        .ToListAsync();
    return Results.Ok(results);
});

app.MapDelete("/admin/purge-all", async (VectorSearchDbContext db, ILogger<Program> logger) =>
{
    var count = await db.MediaEmbeddings.ExecuteDeleteAsync();
    logger.LogWarning("[ADMIN] Purge-All: {Count} Embeddings gelöscht", count);
    return Results.Ok(new { DeletedEmbeddings = count });
});

app.Run();
