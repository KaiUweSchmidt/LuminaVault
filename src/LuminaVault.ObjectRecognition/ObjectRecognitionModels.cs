namespace LuminaVault.ObjectRecognition;

public record RecognizeRequest(
    Guid MediaId,
    string ContentType,
    string StorageBucket,
    string StorageKey);

public record RecognizeResponse(
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
