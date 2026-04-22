using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LuminaVault.GeocodingService.Tests;

public sealed class GeocodingMetadataClientTests
{
    [Fact]
    public async Task WhenUpdateGpsThenSendsPutRequestWithCorrectPath()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new GeocodingMetadataClient(httpClient, NullLogger<GeocodingMetadataClient>.Instance);
        var mediaId = Guid.NewGuid();

        await client.UpdateGpsAsync(mediaId, 48.1351, 11.5820, "München, Deutschland", null);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Put, handler.LastRequest.Method);
        Assert.Equal($"/media/{mediaId}/gps", handler.LastRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task WhenUpdateGpsThenRequestBodyContainsGpsFields()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new GeocodingMetadataClient(httpClient, NullLogger<GeocodingMetadataClient>.Instance);

        await client.UpdateGpsAsync(Guid.NewGuid(), 48.1351, 11.5820, "TestLocation", null);

        Assert.NotNull(handler.LastRequest?.Content);
        var body = await handler.LastRequest.Content.ReadAsStringAsync();
        Assert.Contains("48.1351", body);
        Assert.Contains("11.582", body);
        Assert.Contains("TestLocation", body);
    }

    [Fact]
    public async Task WhenUpdateGpsWithCapturedAtThenRequestBodyContainsCapturedAt()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new GeocodingMetadataClient(httpClient, NullLogger<GeocodingMetadataClient>.Instance);
        var capturedAt = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.Zero);

        await client.UpdateGpsAsync(Guid.NewGuid(), 48.0, 11.0, "Test", capturedAt);

        var body = await handler.LastRequest!.Content!.ReadAsStringAsync();
        Assert.Contains("2024", body);
    }

    [Fact]
    public async Task WhenServerReturnsErrorThenDoesNotThrow()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.InternalServerError, "");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new GeocodingMetadataClient(httpClient, NullLogger<GeocodingMetadataClient>.Instance);

        // Should not throw — errors are logged and swallowed
        await client.UpdateGpsAsync(Guid.NewGuid(), 48.0, 11.0, "Test", null);
    }

    [Fact]
    public async Task WhenLocationNameIsNullThenStillSendsRequest()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new GeocodingMetadataClient(httpClient, NullLogger<GeocodingMetadataClient>.Instance);

        await client.UpdateGpsAsync(Guid.NewGuid(), 48.0, 11.0, null, null);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Put, handler.LastRequest.Method);
    }
}
