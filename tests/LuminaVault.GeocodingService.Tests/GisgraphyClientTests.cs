using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LuminaVault.GeocodingService.Tests;

public sealed class GisgraphyClientTests
{
    [Fact]
    public async Task WhenGisgraphyReturnsNameAndCountryThenLocationIsCombined()
    {
        var json = """{"result":[{"name":"München","countryName":"Deutschland"}]}""";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, json);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new GisgraphyClient(httpClient, NullLogger<GisgraphyClient>.Instance);

        var location = await client.ReverseGeocodeAsync(48.1351, 11.5820);

        Assert.Equal("München, Deutschland", location);
    }

    [Fact]
    public async Task WhenGisgraphyReturnsOnlyNameThenLocationIsName()
    {
        var json = """{"result":[{"name":"München"}]}""";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, json);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new GisgraphyClient(httpClient, NullLogger<GisgraphyClient>.Instance);

        var location = await client.ReverseGeocodeAsync(48.1351, 11.5820);

        Assert.Equal("München", location);
    }

    [Fact]
    public async Task WhenGisgraphyReturnsOnlyCountryThenLocationIsCountry()
    {
        var json = """{"result":[{"countryName":"Deutschland"}]}""";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, json);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new GisgraphyClient(httpClient, NullLogger<GisgraphyClient>.Instance);

        var location = await client.ReverseGeocodeAsync(48.0, 11.0);

        Assert.Equal("Deutschland", location);
    }

    [Fact]
    public async Task WhenGisgraphyReturnsEmptyNameAndCountryThenFallsBackToFormattedFull()
    {
        var json = """{"result":[{"name":"","countryName":"","formattedFull":"48.0, 11.0 Remote Area"}]}""";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, json);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new GisgraphyClient(httpClient, NullLogger<GisgraphyClient>.Instance);

        var location = await client.ReverseGeocodeAsync(48.0, 11.0);

        Assert.Equal("48.0, 11.0 Remote Area", location);
    }

    [Fact]
    public async Task WhenGisgraphyReturnsEmptyResultThenReturnsNull()
    {
        var json = """{"result":[]}""";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, json);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new GisgraphyClient(httpClient, NullLogger<GisgraphyClient>.Instance);

        var location = await client.ReverseGeocodeAsync(48.0, 11.0);

        Assert.Null(location);
    }

    [Fact]
    public async Task WhenGisgraphyReturnsNullResultThenReturnsNull()
    {
        var json = """{}""";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, json);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new GisgraphyClient(httpClient, NullLogger<GisgraphyClient>.Instance);

        var location = await client.ReverseGeocodeAsync(48.0, 11.0);

        Assert.Null(location);
    }

    [Fact]
    public async Task WhenGisgraphyReturnsHttpErrorThenReturnsNull()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.InternalServerError, "");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new GisgraphyClient(httpClient, NullLogger<GisgraphyClient>.Instance);

        var location = await client.ReverseGeocodeAsync(48.0, 11.0);

        Assert.Null(location);
    }

    [Fact]
    public async Task WhenGisgraphyReturnsServiceUnavailableThenReturnsNull()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.ServiceUnavailable, "");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new GisgraphyClient(httpClient, NullLogger<GisgraphyClient>.Instance);

        var location = await client.ReverseGeocodeAsync(48.0, 11.0);

        Assert.Null(location);
    }

    [Fact]
    public async Task WhenGisgraphyReturnsInvalidJsonThenReturnsNull()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "not-json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new GisgraphyClient(httpClient, NullLogger<GisgraphyClient>.Instance);

        var location = await client.ReverseGeocodeAsync(48.0, 11.0);

        Assert.Null(location);
    }

    [Fact]
    public async Task WhenCalledThenRequestUrlContainsLatAndLng()
    {
        var json = """{"result":[{"name":"Test"}]}""";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, json);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new GisgraphyClient(httpClient, NullLogger<GisgraphyClient>.Instance);

        await client.ReverseGeocodeAsync(48.1351, 11.5820);

        Assert.NotNull(handler.LastRequest);
        var url = handler.LastRequest.RequestUri!.ToString();
        Assert.Contains("lat=48.1351", url);
        Assert.Contains("lng=11.582", url);
        Assert.Contains("format=json", url);
    }

    [Fact]
    public async Task WhenGisgraphyReturnsMultipleResultsThenUsesFirstResult()
    {
        var json = """{"result":[{"name":"First","countryName":"A"},{"name":"Second","countryName":"B"}]}""";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, json);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new GisgraphyClient(httpClient, NullLogger<GisgraphyClient>.Instance);

        var location = await client.ReverseGeocodeAsync(48.0, 11.0);

        Assert.Equal("First, A", location);
    }
}
