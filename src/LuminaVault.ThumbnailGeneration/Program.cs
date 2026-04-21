using LuminaVault.ThumbnailGeneration;
using Microsoft.AspNetCore.Mvc;
using Minio;
using Minio.DataModel.Args;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNatsClient();

builder.Services.AddHostedService<NatsThumbnailSubscriber>();

builder.Services.AddSingleton<IMinioClient>(sp =>
{
    var config = builder.Configuration.GetSection("Minio");
    return new MinioClient()
        .WithEndpoint(config["Endpoint"])
        .WithCredentials(config["AccessKey"], config["SecretKey"])
        .Build();
});

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

        var isVideo = request.ContentType?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true;
        MemoryStream outputStream;

        if (isVideo)
        {
            outputStream = await ExtractVideoFrameAsync(sourceStream, thumbnailWidth, thumbnailHeight, logger, request.MediaId);
        }
        else
        {
            using var image = await Image.LoadAsync(sourceStream);
            image.Mutate(ctx => ctx.Resize(new ResizeOptions
            {
                Size = new Size(thumbnailWidth, thumbnailHeight),
                Mode = ResizeMode.Max
            }));

            outputStream = new MemoryStream();
            await image.SaveAsJpegAsync(outputStream);
            outputStream.Position = 0;
        }

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
        var ms = new MemoryStream();
        await minio.GetObjectAsync(new GetObjectArgs()
            .WithBucket(thumbnailBucket)
            .WithObject(thumbnailKey)
            .WithCallbackStream(stream => stream.CopyTo(ms)));
        ms.Position = 0;

        return Results.File(ms, "image/jpeg");
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

/// <summary>
/// Extracts the 15th frame from a video stream using FFmpeg and returns a resized JPEG thumbnail.
/// </summary>
static async Task<MemoryStream> ExtractVideoFrameAsync(Stream videoStream, int width, int height, ILogger logger, Guid mediaId)
{
    var tempInput = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.tmp");
    var tempOutput = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.jpg");
    try
    {
        await using (var fs = File.Create(tempInput))
        {
            await videoStream.CopyToAsync(fs);
        }

        // Use select filter to pick the 15th frame (0-indexed: frame 14)
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-i \"{tempInput}\" -vf \"select=eq(n\\,14),scale={width}:{height}:force_original_aspect_ratio=decrease\" -frames:v 1 -y \"{tempOutput}\"",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = System.Diagnostics.Process.Start(psi)!;
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0 || !File.Exists(tempOutput))
        {
            logger.LogWarning("FFmpeg exited with code {ExitCode} for MediaId={MediaId}: {Stderr}",
                process.ExitCode, mediaId, stderr);
            throw new InvalidOperationException($"FFmpeg failed with exit code {process.ExitCode}");
        }

        var result = new MemoryStream();
        await using (var outFs = File.OpenRead(tempOutput))
        {
            await outFs.CopyToAsync(result);
        }
        result.Position = 0;
        return result;
    }
    finally
    {
        if (File.Exists(tempInput)) File.Delete(tempInput);
        if (File.Exists(tempOutput)) File.Delete(tempOutput);
    }
}
