namespace LuminaVault.FaceRecognition;

public record FaceRecognizeRequest(
    Guid MediaId,
    string StorageBucket,
    string StorageKey);

public record FaceRecognizeResponse(
    Guid MediaId,
    int PersonCount,
    List<FaceInfo> Faces);

public record FaceInfo(string FaceDescription, double BboxX, double BboxY, double BboxWidth, double BboxHeight);

public record OllamaGenerateRequest(
    string Model,
    string Prompt,
    string[] Images,
    bool Stream,
    string? Format = null);

public record OllamaGenerateResponse(string Response);
