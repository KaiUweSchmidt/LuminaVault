using System.Net;

namespace LuminaVault.GeocodingService.Tests;

/// <summary>
/// Reusable fake HTTP message handler for unit tests.
/// Returns a fixed status code and content for every request.
/// </summary>
internal sealed class FakeHttpMessageHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content)
        };
        return Task.FromResult(response);
    }
}
