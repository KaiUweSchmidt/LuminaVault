using System.Text.Json;
using System.Text.Json.Serialization;

namespace LuminaVault.ObjectRecognition;

/// <summary>
/// Uses Ollama vision models to detect objects in images (persons, cats, dogs, etc.).
/// </summary>
public class OllamaClient(HttpClient httpClient, ILogger<OllamaClient> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Detects objects in the image and returns a list of detected object labels
    /// along with a flag indicating whether a person was found.
    /// </summary>
    public async Task<OllamaDetectionResult> DetectObjectsAsync(string base64Image, string model = "llava:7b")
    {
        const string prompt =
            "List all objects visible in this image (e.g. person, cat, dog, car, chair, tree). " +
            "Respond ONLY with JSON in this exact format: " +
            "{\"objects\": [\"person\", \"cat\"], \"person_detected\": true}";

        var request = new OllamaGenerateRequest(model, prompt, [base64Image], false, "json");

        logger.LogInformation("[PIPELINE:Ollama] DetectObjects → POST /api/generate (Model={Model})", model);
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var response = await httpClient.PostAsJsonAsync("/api/generate", request, JsonOptions);
            sw.Stop();
            logger.LogInformation("[PIPELINE:Ollama] DetectObjects ← {StatusCode} in {ElapsedMs}ms",
                response.StatusCode, sw.ElapsedMilliseconds);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(JsonOptions);
            logger.LogInformation("[PIPELINE:Ollama] DetectObjects Rohantwort: {Raw}", result?.Response ?? "(null)");

            if (string.IsNullOrWhiteSpace(result?.Response))
                return new OllamaDetectionResult([], false);

            var parsed = JsonSerializer.Deserialize<OllamaDetectionResult>(result.Response,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return parsed ?? new OllamaDetectionResult([], false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[PIPELINE:Ollama] DetectObjects FEHLGESCHLAGEN");
            return new OllamaDetectionResult([], false);
        }
    }
}
