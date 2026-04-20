using System.Text.Json;
using System.Text.Json.Serialization;

namespace LuminaVault.ObjectRecognition;

public class OllamaClient(HttpClient httpClient, ILogger<OllamaClient> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<int> CountPersonsAsync(string base64Image, string model = "llava")
    {
        const string prompt =
            "Count the number of people (humans) visible in this image. " +
            "Respond with only a JSON object in this exact format: {\"personCount\": <number>}. " +
            "If there are no people, respond with {\"personCount\": 0}.";

        var request = new OllamaGenerateRequest(model, prompt, [base64Image], false, "json");

        try
        {
            var response = await httpClient.PostAsJsonAsync("/api/generate", request, JsonOptions);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(JsonOptions);
            if (result?.Response is null) return 0;

            var parsed = JsonSerializer.Deserialize<PersonCountResult>(result.Response,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return parsed?.PersonCount ?? 0;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to count persons via Ollama, returning 0");
            return 0;
        }
    }

    public async Task<string> DescribeFaceAsync(string base64Image, int faceIndex, string model = "llava")
    {
        var prompt =
            $"Describe the appearance of person #{faceIndex + 1} in this image in detail. " +
            "Include observable physical characteristics such as approximate age range, hair color and style, " +
            "facial features, and any distinguishing characteristics. " +
            "Respond with a concise description in one paragraph.";

        var request = new OllamaGenerateRequest(model, prompt, [base64Image], false);

        try
        {
            var response = await httpClient.PostAsJsonAsync("/api/generate", request, JsonOptions);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(JsonOptions);
            return result?.Response?.Trim() ?? string.Empty;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to describe face via Ollama");
            return string.Empty;
        }
    }
}
