using System.Text.Json;
using System.Text.Json.Serialization;

namespace LuminaVault.FaceRecognition;

/// <summary>
/// Uses Ollama vision models to describe faces in images. Face detection and bounding boxes
/// are handled by YOLO — this client only generates textual descriptions.
/// </summary>
public class OllamaClient(HttpClient httpClient, ILogger<OllamaClient> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Describes a face at the given bounding box region in the image using the Ollama vision model.
    /// </summary>
    public async Task<string> DescribeFaceAsync(string base64Image, int faceIndex, int totalFaces, string model = "llava:7b")
    {
        var prompt = totalFaces == 1
            ? "Describe the person's appearance in this image in one brief sentence (age, gender, hair, clothing, expression). Respond ONLY with JSON: {\"description\": \"...\"}"
            : $"There are {totalFaces} people in this image. Describe person #{faceIndex + 1}'s appearance in one brief sentence (age, gender, hair, clothing, expression). Respond ONLY with JSON: {{\"description\": \"...\"}}";

        var request = new OllamaGenerateRequest(model, prompt, [base64Image], false, "json");

        logger.LogInformation("[PIPELINE:Ollama] DescribeFace #{FaceIndex} → POST /api/generate (Model={Model})",
            faceIndex + 1, model);
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var response = await httpClient.PostAsJsonAsync("/api/generate", request, JsonOptions);
            sw.Stop();
            logger.LogInformation("[PIPELINE:Ollama] DescribeFace #{FaceIndex} ← {StatusCode} in {ElapsedMs}ms",
                faceIndex + 1, response.StatusCode, sw.ElapsedMilliseconds);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(JsonOptions);
            logger.LogInformation("[PIPELINE:Ollama] DescribeFace #{FaceIndex} Rohantwort: {Raw}",
                faceIndex + 1, result?.Response ?? "(null)");
            if (string.IsNullOrWhiteSpace(result?.Response))
                return string.Empty;

            var parsed = JsonSerializer.Deserialize<DescriptionResult>(result.Response,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return parsed?.Description ?? string.Empty;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[PIPELINE:Ollama] DescribeFace #{FaceIndex} FEHLGESCHLAGEN", faceIndex + 1);
            return string.Empty;
        }
    }
}

public record DescriptionResult(string? Description);
