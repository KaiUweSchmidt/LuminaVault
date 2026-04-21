namespace LuminaVault.ServiceDefaults;

/// <summary>
/// Well-known NATS subject constants shared across all services.
/// </summary>
public static class NatsSubjects
{
    /// <summary>Published by media-import after a file is successfully stored (JetStream).</summary>
    public const string MediaImported = "media.imported";

    /// <summary>Published by any pipeline step after it finishes processing a media item (Core NATS, fire-and-forget).</summary>
    public const string PipelineStepCompleted = "media.pipeline.step.completed";
}

/// <summary>
/// Well-known JetStream stream names.
/// </summary>
public static class NatsStreams
{
    /// <summary>Stream that persists media import events for durable processing.</summary>
    public const string MediaPipeline = "MEDIA_PIPELINE";
}

/// <summary>
/// Well-known JetStream durable consumer names.
/// </summary>
public static class NatsConsumers
{
    public const string ThumbnailGeneration = "thumbnail-generation";
    public const string ObjectRecognition = "object-recognition";
}
