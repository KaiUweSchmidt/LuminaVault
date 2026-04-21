namespace LuminaVault.ObjectRecognition;

public record RecognizeRequest(
    Guid MediaId,
    string ContentType,
    string StorageBucket,
    string StorageKey);

public record RecognizeResponse(
    Guid MediaId,
    bool PersonDetected,
    List<string> DetectedObjects);

/// <summary>
/// Result from the YOLO object detector containing detected labels and a person flag.
/// </summary>
public record YoloDetectionResult(List<string> Objects, bool PersonDetected);
