namespace LuminaVault.WebUI.Services;

public class MediaApiClient(HttpClient httpClient)
{
    public async Task<List<MediaItem>> GetMediaAsync(string? tag = null, string? search = null)
    {
        var query = string.Empty;
        if (!string.IsNullOrWhiteSpace(tag)) query += $"?tag={Uri.EscapeDataString(tag)}";
        if (!string.IsNullOrWhiteSpace(search))
            query += (query.Length > 0 ? "&" : "?") + $"search={Uri.EscapeDataString(search)}";
        return await httpClient.GetFromJsonAsync<List<MediaItem>>($"/api/metadata/media{query}") ?? [];
    }

    public async Task<MediaItem?> GetMediaByIdAsync(Guid id) =>
        await httpClient.GetFromJsonAsync<MediaItem>($"/api/metadata/media/{id}");

    public async Task<List<SearchResult>> SearchAsync(float[] embedding, int topK = 10)
    {
        var response = await httpClient.PostAsJsonAsync("/api/search/search", new { Embedding = embedding, TopK = topK });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<SearchResult>>() ?? [];
    }

    public async Task<List<Face>> GetFacesAsync(Guid mediaId) =>
        await httpClient.GetFromJsonAsync<List<Face>>($"/api/metadata/faces/{mediaId}") ?? [];

    public async Task<bool> UpdateFaceNameAsync(Guid faceId, string? name)
    {
        var response = await httpClient.PutAsJsonAsync($"/api/metadata/faces/{faceId}", new { Name = name });
        return response.IsSuccessStatusCode;
    }

    public async Task<List<MediaItem>> FindSimilarPersonsAsync(Guid mediaId) =>
        await httpClient.GetFromJsonAsync<List<MediaItem>>($"/api/metadata/faces/similar/{mediaId}") ?? [];
}

public record MediaItem(
    Guid Id,
    string Title,
    string Description,
    string[] Tags,
    double? GpsLatitude,
    double? GpsLongitude,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    int? PersonCount = null);

public record Face(
    Guid Id,
    Guid MediaId,
    string? Name,
    string FaceDescription,
    DateTimeOffset DetectedAt);

public record SearchResult(Guid MediaId, double Distance);
