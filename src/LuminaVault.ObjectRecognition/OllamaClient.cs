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

    public async Task<int> CountPersonsAsync(string base64Image, string model = "llava:13b")
    {
        const string prompt =
            "Look at this image very carefully. Count only the faces you can see. " +
            "A face must be visible from the front or side — do NOT count people seen from behind or whose face is not visible. " +
            "Include partially visible, small, or blurry faces as long as facial features (eyes, nose, or mouth) are recognizable. " +
            "Respond with only a JSON object in this exact format: {\"personCount\": <number>}. " +
            "If there are no visible faces, respond with {\"personCount\": 0}.";

        var request = new OllamaGenerateRequest(model, prompt, [base64Image], false, "json");

        logger.LogInformation("[PIPELINE:Ollama] CountPersons → POST /api/generate (Model={Model}, ImageSize={ImageSize} Zeichen)",
            model, base64Image.Length);
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var response = await httpClient.PostAsJsonAsync("/api/generate", request, JsonOptions);
            sw.Stop();
            logger.LogInformation("[PIPELINE:Ollama] CountPersons ← {StatusCode} in {ElapsedMs}ms",
                response.StatusCode, sw.ElapsedMilliseconds);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(JsonOptions);
            logger.LogInformation("[PIPELINE:Ollama] CountPersons Rohantwort: {RawResponse}", result?.Response ?? "(null)");
            if (result?.Response is null) return 0;

            var parsed = JsonSerializer.Deserialize<PersonCountResult>(result.Response,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            var count = parsed?.PersonCount ?? 0;
            logger.LogInformation("[PIPELINE:Ollama] CountPersons Ergebnis: {PersonCount} Person(en)", count);
            return count;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[PIPELINE:Ollama] CountPersons FEHLGESCHLAGEN, gebe 0 zurück");
            return 0;
        }
    }

    public async Task<string> DescribeFaceAsync(string base64Image, int faceIndex, string model = "llava:13b")
    {
        var prompt =
            $"Describe the appearance of person #{faceIndex + 1} in this image in detail. " +
            "Include observable physical characteristics such as approximate age range, hair color and style, " +
            "facial features, and any distinguishing characteristics. " +
            "Respond with a concise description in one paragraph.";

        var request = new OllamaGenerateRequest(model, prompt, [base64Image], false);

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
            var description = result?.Response?.Trim() ?? string.Empty;
            logger.LogInformation("[PIPELINE:Ollama] DescribeFace #{FaceIndex} Ergebnis: {DescriptionPreview}",
                faceIndex + 1, description.Length > 100 ? description[..100] + "..." : description);
            return description;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[PIPELINE:Ollama] DescribeFace #{FaceIndex} FEHLGESCHLAGEN", faceIndex + 1);
            return string.Empty;
        }
    }
}
