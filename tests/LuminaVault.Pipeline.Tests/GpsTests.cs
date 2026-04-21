using System.Net;
using LuminaVault.MediaImport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LuminaVault.Pipeline.Tests;

public sealed class GpsTests
{
    [Fact]
    public void WhenStreamIsEmptyThenExtractGpsReturnsNull()
    {
        using var stream = new MemoryStream([]);
        var result = GpsExifExtractor.ExtractGps(stream);
        Assert.Null(result);
    }

    [Fact]
    public void WhenStreamHasNoExifThenExtractGpsReturnsNull()
    {
        // Minimal JPEG magic bytes without EXIF GPS
        using var stream = new MemoryStream([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10]);
        var result = GpsExifExtractor.ExtractGps(stream);
        Assert.Null(result);
    }

    [Fact]
    public async Task WhenNominatimReturnsValidResponseThenLocationIsBuiltFromCityAndCountry()
    {
        var json = """{"display_name":"Test Street, Munich, Bavaria, Germany","address":{"city":"Munich","country":"Germany"}}""";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, json);
        var httpClient = new HttpClient(handler);
        var service = new NominatimGeocodingService(httpClient, NullLogger<NominatimGeocodingService>.Instance);

        var location = await service.GetLocationNameAsync(48.1351, 11.5820);

        Assert.Equal("Munich, Germany", location);
    }

    [Fact]
    public async Task WhenNominatimReturnsTownThenLocationUsesTown()
    {
        var json = """{"display_name":"Some Street, Smalltown, Germany","address":{"town":"Smalltown","country":"Germany"}}""";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, json);
        var httpClient = new HttpClient(handler);
        var service = new NominatimGeocodingService(httpClient, NullLogger<NominatimGeocodingService>.Instance);

        var location = await service.GetLocationNameAsync(48.0, 11.0);

        Assert.Equal("Smalltown, Germany", location);
    }

    [Fact]
    public async Task WhenNominatimReturnsErrorThenLocationIsNull()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.ServiceUnavailable, string.Empty);
        var httpClient = new HttpClient(handler);
        var service = new NominatimGeocodingService(httpClient, NullLogger<NominatimGeocodingService>.Instance);

        var location = await service.GetLocationNameAsync(48.0, 11.0);

        Assert.Null(location);
    }

    [Fact]
    public async Task WhenNominatimAddressHasOnlyCountryThenLocationUsesCountry()
    {
        var json = """{"display_name":"Remote Area, Germany","address":{"country":"Germany"}}""";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, json);
        var httpClient = new HttpClient(handler);
        var service = new NominatimGeocodingService(httpClient, NullLogger<NominatimGeocodingService>.Instance);

        var location = await service.GetLocationNameAsync(48.0, 11.0);

        Assert.Equal("Germany", location);
    }

    private sealed class FakeHttpMessageHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content)
            };
            return Task.FromResult(response);
        }
    }
}
