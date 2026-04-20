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

    /// <summary>
    /// Uploads a media file (photo or video) to the import service.
    /// </summary>
    public async Task<ImportResult> UploadMediaAsync(Stream fileStream, string fileName, string contentType, string title, CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();
        using var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        content.Add(streamContent, "file", fileName);
        content.Add(new StringContent(title), "title");

        var response = await httpClient.PostAsync("/api/media/import", content, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ImportResult>(cancellationToken) ?? throw new InvalidOperationException("Empty response from import service.");
    }

    /// <summary>
    /// Gets the thumbnail URL for a media item.
    /// </summary>
    public async Task<string?> GetThumbnailUrlAsync(Guid mediaId)
    {
        try
        {
            var result = await httpClient.GetFromJsonAsync<ThumbnailResult>($"/api/thumbnails/thumbnails/{mediaId}");
            return result?.Url;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the list of imported media items from the MediaImport service.
    /// </summary>
    public async Task<List<ImportedMediaItem>> GetImportedMediaAsync()
    {
        return await httpClient.GetFromJsonAsync<List<ImportedMediaItem>>("/api/media/media") ?? [];
    }

    /// <summary>
    /// Gets a presigned URL for the original media file.
    /// </summary>
    public async Task<string?> GetOriginalUrlAsync(Guid mediaId)
    {
        try
        {
            var result = await httpClient.GetFromJsonAsync<PresignedUrlResult>($"/api/media/media/{mediaId}/url");
            return result?.Url;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }
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

public record ImportResult(Guid Id, string FileName, string ContentType, long FileSizeBytes, DateTimeOffset ImportedAt, string StorageBucket, string StorageKey);

public record ImportedMediaItem(Guid Id, string FileName, string ContentType, long FileSizeBytes, DateTimeOffset ImportedAt, string StorageBucket, string StorageKey);

public record ThumbnailResult(string Url);

public record PresignedUrlResult(string Url);