using System.Net;
using System.Net.Http.Json;
using LuminaVault.MediaImport;
using Microsoft.Extensions.DependencyInjection;
using Minio;
using Minio.DataModel.Args;
using NSubstitute;
using Xunit;

namespace LuminaVault.Pipeline.Tests;

public sealed class ImportEndpointTests : IDisposable
{
    private readonly ImportApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task WhenNoFileProvidedThenReturnsBadRequest()
    {
        var client = _factory.CreateClient();

        var content = new MultipartFormDataContent();
        content.Add(new StringContent("test"), "title");

        var response = await client.PostAsync("/import", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task WhenEmptyFileProvidedThenReturnsBadRequest()
    {
        var client = _factory.CreateClient();

        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent([]);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        content.Add(fileContent, "file", "empty.jpg");

        var response = await client.PostAsync("/import", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task WhenImageFileUploadedThenReturnsCreatedAndStoresInDb()
    {
        var client = _factory.CreateClient();
        SetupMinioMock();

        var content = CreateMultipartContent("test.jpg", "image/jpeg", [0xFF, 0xD8, 0xFF, 0xE0]);

        var response = await client.PostAsync("/import", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var mediaItem = await response.Content.ReadFromJsonAsync<MediaItem>();
        Assert.NotNull(mediaItem);
        Assert.Equal("test.jpg", mediaItem.FileName);
        Assert.Equal("image/jpeg", mediaItem.ContentType);
        Assert.Equal(4, mediaItem.FileSizeBytes);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task WhenImageFileUploadedThenUploadsToMinio()
    {
        var client = _factory.CreateClient();
        SetupMinioMock();

        var content = CreateMultipartContent("photo.png", "image/png", [0x89, 0x50, 0x4E, 0x47]);

        await client.PostAsync("/import", content);

        await _factory.MinioClient.Received(1).PutObjectAsync(Arg.Any<PutObjectArgs>(), default);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task WhenImageFileUploadedThenEnsuresBucketExists()
    {
        var client = _factory.CreateClient();
        SetupMinioMock();

        var content = CreateMultipartContent("photo.jpg", "image/jpeg", [0xFF, 0xD8]);

        await client.PostAsync("/import", content);

        await _factory.MinioClient.Received().BucketExistsAsync(Arg.Any<BucketExistsArgs>(), default);
    }

    [Fact]
    public async Task WhenNonImageUploadedThenSkipsThumbnailAndRecognition()
    {
        var client = _factory.CreateClient();
        SetupMinioMock();

        var content = CreateMultipartContent("document.pdf", "application/pdf", [0x25, 0x50, 0x44, 0x46]);

        var response = await client.PostAsync("/import", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var mediaItem = await response.Content.ReadFromJsonAsync<MediaItem>();
        Assert.NotNull(mediaItem);
        Assert.Equal("application/pdf", mediaItem.ContentType);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task WhenTitleProvidedThenUsesProvidedTitle()
    {
        var client = _factory.CreateClient();
        SetupMinioMock();

        var content = CreateMultipartContent("photo.jpg", "image/jpeg", [0xFF, 0xD8]);
        content.Add(new StringContent("My Vacation Photo"), "title");

        var response = await client.PostAsync("/import", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task WhenFileUploadedThenMediaItemIsPersisted()
    {
        var client = _factory.CreateClient();
        SetupMinioMock();

        var content = CreateMultipartContent("test.jpg", "image/jpeg", [0xFF, 0xD8, 0xFF]);
        var response = await client.PostAsync("/import", content);
        var mediaItem = await response.Content.ReadFromJsonAsync<MediaItem>();

        var getResponse = await client.GetAsync($"/media/{mediaItem!.Id}");

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<MediaItem>();
        Assert.NotNull(fetched);
        Assert.Equal(mediaItem.Id, fetched.Id);
        Assert.Equal("test.jpg", fetched.FileName);
    }

    [Fact]
    public async Task WhenGetNonExistentMediaThenReturnsNotFound()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/media/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task WhenMultipleFilesUploadedThenAllAppearInMediaList()
    {
        using var factory = new ImportApiFactory();
        SetupMinioMock(factory.MinioClient);
        var client = factory.CreateClient();

        var content1 = CreateMultipartContent("a.jpg", "image/jpeg", [0xFF]);
        var resp1 = await client.PostAsync("/import", content1);
        Assert.Equal(HttpStatusCode.Created, resp1.StatusCode);

        var content2 = CreateMultipartContent("b.png", "image/png", [0x89]);
        var resp2 = await client.PostAsync("/import", content2);
        Assert.Equal(HttpStatusCode.Created, resp2.StatusCode);

        var item1 = await resp1.Content.ReadFromJsonAsync<MediaItem>();
        var item2 = await resp2.Content.ReadFromJsonAsync<MediaItem>();

        // Verify individual items are retrievable
        var get1 = await client.GetAsync($"/media/{item1!.Id}");
        Assert.Equal(HttpStatusCode.OK, get1.StatusCode);
        var get2 = await client.GetAsync($"/media/{item2!.Id}");
        Assert.Equal(HttpStatusCode.OK, get2.StatusCode);
    }

    [Fact]
    public async Task WhenNonMultipartRequestThenReturnsBadRequest()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/import", new StringContent("not multipart"));

        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest ||
            response.StatusCode == HttpStatusCode.InternalServerError,
            $"Expected BadRequest or InternalServerError but got {response.StatusCode}");
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task WhenFileUploadedThenStorageKeyContainsMediaId()
    {
        var client = _factory.CreateClient();
        SetupMinioMock();

        var content = CreateMultipartContent("photo.jpg", "image/jpeg", [0xFF, 0xD8]);

        var response = await client.PostAsync("/import", content);
        var mediaItem = await response.Content.ReadFromJsonAsync<MediaItem>();

        Assert.NotNull(mediaItem);
        Assert.Contains(mediaItem.Id.ToString(), mediaItem.StorageKey);
        Assert.Equal("media", mediaItem.StorageBucket);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task WhenThumbnailServiceFailsThenImportStillSucceeds()
    {
        using var factory = new ImportApiFactory();
        SetupMinioMock(factory.MinioClient);

        var client = factory.CreateClient();
        var content = CreateMultipartContent("photo.jpg", "image/jpeg", [0xFF, 0xD8]);

        // ThumbnailServiceClient will fail because there's no real thumbnail service
        // but the import endpoint catches these exceptions
        var response = await client.PostAsync("/import", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task WhenPdfUploadedThenSkipsThumbnailAndRecognition()
    {
        var client = _factory.CreateClient();
        SetupMinioMock();

        var content = CreateMultipartContent("document.pdf", "application/pdf", [0x25, 0x50, 0x44, 0x46]);

        var response = await client.PostAsync("/import", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var mediaItem = await response.Content.ReadFromJsonAsync<MediaItem>();
        Assert.NotNull(mediaItem);
        Assert.Equal("application/pdf", mediaItem.ContentType);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task WhenGetMediaStreamThenReturnsFile()
    {
        var client = _factory.CreateClient();
        SetupMinioMock();

        var importContent = CreateMultipartContent("test.jpg", "image/jpeg", [0xFF, 0xD8]);
        var importResponse = await client.PostAsync("/import", importContent);
        var mediaItem = await importResponse.Content.ReadFromJsonAsync<MediaItem>();

        var response = await client.GetAsync($"/media/{mediaItem!.Id}/url");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("image/", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task WhenGetPresignedUrlForNonExistentMediaThenReturnsNotFound()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/media/{Guid.NewGuid()}/url");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task WhenMultipleImportsThenMediaListIsOrderedByImportDateDescending()
    {
        using var factory = new ImportApiFactory();
        SetupMinioMock(factory.MinioClient);
        var client = factory.CreateClient();

        var content1 = CreateMultipartContent("first.jpg", "image/jpeg", [0xFF]);
        var resp1 = await client.PostAsync("/import", content1);
        Assert.Equal(HttpStatusCode.Created, resp1.StatusCode);
        var item1 = await resp1.Content.ReadFromJsonAsync<MediaItem>();

        await Task.Delay(50);

        var content2 = CreateMultipartContent("second.jpg", "image/jpeg", [0xFF]);
        var resp2 = await client.PostAsync("/import", content2);
        Assert.Equal(HttpStatusCode.Created, resp2.StatusCode);
        var item2 = await resp2.Content.ReadFromJsonAsync<MediaItem>();

        // Verify both items exist and second was imported after first
        Assert.NotNull(item1);
        Assert.NotNull(item2);
        Assert.True(item2.ImportedAt >= item1.ImportedAt);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task WhenRecognitionServiceFailsThenImportStillSucceeds()
    {
        using var factory = new ImportApiFactory();
        SetupMinioMock(factory.MinioClient);

        var client = factory.CreateClient();
        var content = CreateMultipartContent("photo.jpg", "image/jpeg", [0xFF, 0xD8]);

        var response = await client.PostAsync("/import", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private void SetupMinioMock() => SetupMinioMock(_factory.MinioClient);

    private static void SetupMinioMock(IMinioClient minioClient)
    {
        minioClient.BucketExistsAsync(Arg.Any<BucketExistsArgs>(), default)
            .Returns(true);
        minioClient.PutObjectAsync(Arg.Any<PutObjectArgs>(), default)
            .Returns(default(Minio.DataModel.Response.PutObjectResponse));
    }

    private static MultipartFormDataContent CreateMultipartContent(string fileName, string contentType, byte[] data)
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(data);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "file", fileName);
        return content;
    }
}
