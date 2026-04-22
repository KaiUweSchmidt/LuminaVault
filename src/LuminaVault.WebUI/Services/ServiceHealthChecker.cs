namespace LuminaVault.WebUI.Services;

/// <summary>
/// Checks the health of all pipeline services via their /health endpoints (through the API gateway).
/// </summary>
public sealed class ServiceHealthChecker(HttpClient httpClient)
{
    private static readonly (string Name, string HealthPath)[] Services =
    [
        ("Media-Import", "/api/media/health"),
        ("Metadata-Storage", "/api/metadata/health"),
        ("Thumbnail-Generation", "/api/thumbnails/health"),
        ("Object-Recognition", "/api/recognition/health"),
        ("Face-Recognition", "/api/facerecognition/health"),
        ("Geocoding-Service", "/api/geocoding/health"),
        ("Vector-Search", "/api/search/health"),
    ];

    /// <summary>
    /// Probes the /health endpoint of every known pipeline service.
    /// </summary>
    public async Task<IReadOnlyList<ServiceHealthInfo>> CheckAllAsync(CancellationToken ct = default)
    {
        var tasks = Services.Select(async s =>
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(5));

                var response = await httpClient.GetAsync(s.HealthPath, cts.Token);
                return new ServiceHealthInfo
                {
                    ServiceName = s.Name,
                    IsHealthy = response.IsSuccessStatusCode,
                    StatusCode = (int)response.StatusCode,
                };
            }
            catch (Exception ex)
            {
                return new ServiceHealthInfo
                {
                    ServiceName = s.Name,
                    IsHealthy = false,
                    Error = ex is TaskCanceledException ? "Timeout" : ex.Message,
                };
            }
        });

        return await Task.WhenAll(tasks);
    }
}

/// <summary>Health status of a single pipeline service.</summary>
public sealed class ServiceHealthInfo
{
    /// <summary>Display name of the service.</summary>
    public required string ServiceName { get; init; }

    /// <summary>Whether the service responded with a healthy status.</summary>
    public bool IsHealthy { get; init; }

    /// <summary>HTTP status code (if any).</summary>
    public int? StatusCode { get; init; }

    /// <summary>Error message when the service could not be reached.</summary>
    public string? Error { get; init; }
}
