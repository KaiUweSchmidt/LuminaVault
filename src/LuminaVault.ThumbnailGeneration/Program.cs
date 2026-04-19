using LuminaVault.ThumbnailGeneration;
using Minio;
using Minio.DataModel.Args;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddMinioClient("minio");

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapPost("/thumbnails/generate", async (GenerateThumbnailRequest request, IMinioClient minio, ILogger<Program> logger) =>
{
    const string thumbnailBucket = "thumbnails";
    const int thumbnailWidth = 320;
    const int thumbnailHeight = 240;

    try
    {
        await EnsureBucketExistsAsync(minio, thumbnailBucket);

        var sourceStream = new MemoryStream();
        await minio.GetObjectAsync(new GetObjectArgs()
            .WithBucket(request.Bucket)
            .WithObject(request.StorageKey)
            .WithCallbackStream(stream => stream.CopyTo(sourceStream)));
        sourceStream.Position = 0;

        using var image = await Image.LoadAsync(sourceStream);
        image.Mutate(ctx => ctx.Resize(new ResizeOptions
        {
            Size = new Size(thumbnailWidth, thumbnailHeight),
            Mode = ResizeMode.Max
        }));

        var outputStream = new MemoryStream();
        await image.SaveAsJpegAsync(outputStream);
        outputStream.Position = 0;

        var thumbnailKey = $"{request.MediaId}/thumb.jpg";
        await minio.PutObjectAsync(new PutObjectArgs()
            .WithBucket(thumbnailBucket)
            .WithObject(thumbnailKey)
            .WithStreamData(outputStream)
            .WithObjectSize(outputStream.Length)
            .WithContentType("image/jpeg"));

        logger.LogInformation("Thumbnail generated for media {MediaId}", request.MediaId);
        return Results.Ok(new { MediaId = request.MediaId, ThumbnailKey = thumbnailKey });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to generate thumbnail for media {MediaId}", request.MediaId);
        return Results.Problem("Thumbnail generation failed");
    }
});

app.MapGet("/thumbnails/{mediaId:guid}", async (Guid mediaId, IMinioClient minio) =>
{
    const string thumbnailBucket = "thumbnails";
    var thumbnailKey = $"{mediaId}/thumb.jpg";

    try
    {
        var presignedUrl = await minio.PresignedGetObjectAsync(new PresignedGetObjectArgs()
            .WithBucket(thumbnailBucket)
            .WithObject(thumbnailKey)
            .WithExpiry(3600));
        return Results.Ok(new { Url = presignedUrl });
    }
    catch
    {
        return Results.NotFound();
    }
});

app.Run();

static async Task EnsureBucketExistsAsync(IMinioClient minio, string bucketName)
{
    var exists = await minio.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucketName));
    if (!exists)
        await minio.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucketName));
}
