using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LuminaVault.GeocodingService.Tests;

public sealed class NominatimClientTests
{
    [Fact]
    public async Task WhenNominatimReturnsCityAndCountryThenLocationIsCombined()
    {
        var json = """{"display_name":"Marienplatz, München, Bayern, Deutschland","address":{"city":"München","country":"Deutschland"}}""";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, json);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new NominatimClient(httpClient, NullLogger<NominatimClient>.Instance);

        var location = await client.ReverseGeocodeAsync(48.1351, 11.5820);

        Assert.Equal("München, Deutschland", location);
    }

    [Fact]
    public async Task WhenNominatimReturnsTownThenLocationUsesTown()
    {
        var json = """{"display_name":"Hauptstraße, Erding, Bayern, Deutschland","address":{"town":"Erding","country":"Deutschland"}}""";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, json);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new NominatimClient(httpClient, NullLogger<NominatimClient>.Instance);

        var location = await client.ReverseGeocodeAsync(48.3, 11.9);

        Assert.Equal("Erding, Deutschland", location);
    }

    [Fact]
    public async Task WhenNominatimReturnsVillageThenLocationUsesVillage()
    {
        var json = """{"display_name":"Dorfstraße, Kleindorf, Bayern, Deutschland","address":{"village":"Kleindorf","country":"Deutschland"}}""";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, json);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new NominatimClient(httpClient, NullLogger<NominatimClient>.Instance);

        var location = await client.ReverseGeocodeAsync(48.0, 11.0);

        Assert.Equal("Kleindorf, Deutschland", location);
    }

    [Fact]
    public async Task WhenNominatimReturnsOnlyCountryThenLocationIsCountry()
    {
        var json = """{"display_name":"Deutschland","address":{"country":"Deutschland"}}""";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, json);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new NominatimClient(httpClient, NullLogger<NominatimClient>.Instance);

        var location = await client.ReverseGeocodeAsync(48.0, 11.0);

        Assert.Equal("Deutschland", location);
    }

    [Fact]
    public async Task WhenNominatimReturnsNoAddressThenFallsBackToDisplayName()
    {
        var json = """{"display_name":"48.0, 11.0 Remote Area"}""";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, json);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new NominatimClient(httpClient, NullLogger<NominatimClient>.Instance);

        var location = await client.ReverseGeocodeAsync(48.0, 11.0);

        Assert.Equal("48.0, 11.0 Remote Area", location);
    }

    [Fact]
    public async Task WhenNominatimReturnsErrorThenReturnsNull()
    {
        var json = """{"error":"Unable to geocode"}""";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, json);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new NominatimClient(httpClient, NullLogger<NominatimClient>.Instance);

        var location = await client.ReverseGeocodeAsync(0, 0);

        Assert.Null(location);
    }

    [Fact]
    public async Task WhenNominatimReturnsHttpErrorThenReturnsNull()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.InternalServerError, "");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new NominatimClient(httpClient, NullLogger<NominatimClient>.Instance);

        var location = await client.ReverseGeocodeAsync(48.0, 11.0);

        Assert.Null(location);
    }

    [Fact]
    public async Task WhenNominatimReturnsServiceUnavailableThenReturnsNull()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.ServiceUnavailable, "");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new NominatimClient(httpClient, NullLogger<NominatimClient>.Instance);

        var location = await client.ReverseGeocodeAsync(48.0, 11.0);

        Assert.Null(location);
    }

    [Fact]
    public async Task WhenNominatimReturnsInvalidJsonThenReturnsNull()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "not json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new NominatimClient(httpClient, NullLogger<NominatimClient>.Instance);

        var location = await client.ReverseGeocodeAsync(48.0, 11.0);

        Assert.Null(location);
    }

    [Fact]
    public async Task WhenNominatimReturnsEmptyJsonThenReturnsNull()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "{}");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new NominatimClient(httpClient, NullLogger<NominatimClient>.Instance);

        var location = await client.ReverseGeocodeAsync(48.0, 11.0);

        Assert.Null(location);
    }

    [Fact]
    public async Task WhenCalledThenRequestUrlContainsLatAndLon()
    {
        var json = """{"display_name":"Test","address":{"city":"Test","country":"Test"}}""";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, json);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new NominatimClient(httpClient, NullLogger<NominatimClient>.Instance);

        await client.ReverseGeocodeAsync(48.1351, 11.5820);

        Assert.NotNull(handler.LastRequest);
        var url = handler.LastRequest!.RequestUri!.ToString();
        Assert.Contains("lat=48.1351", url);
        Assert.Contains("lon=11.582", url);
        Assert.Contains("format=jsonv2", url);
    }

    [Fact]
    public async Task WhenNominatimReturnsMunicipalityThenLocationUsesMunicipality()
    {
        var json = """{"display_name":"Gemeinde Test","address":{"municipality":"Gemeinde Test","country":"Deutschland"}}""";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, json);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new NominatimClient(httpClient, NullLogger<NominatimClient>.Instance);

        var location = await client.ReverseGeocodeAsync(48.0, 11.0);

        Assert.Equal("Gemeinde Test, Deutschland", location);
    }

    [Fact]
    public async Task WhenNominatimReturnsCountyFallbackThenLocationUsesCounty()
    {
        var json = """{"display_name":"Landkreis Test","address":{"county":"Landkreis Test","country":"Deutschland"}}""";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, json);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new NominatimClient(httpClient, NullLogger<NominatimClient>.Instance);

        var location = await client.ReverseGeocodeAsync(48.0, 11.0);

        Assert.Equal("Landkreis Test, Deutschland", location);
    }
}
