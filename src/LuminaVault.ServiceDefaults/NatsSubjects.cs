namespace LuminaVault.ServiceDefaults;

/// <summary>
/// Well-known NATS subject constants shared across all services.
/// </summary>
public static class NatsSubjects
{
    /// <summary>Published by media-import after a file is successfully stored.</summary>
    public const string MediaImported = "media.imported";

    /// <summary>Published by any pipeline step after it finishes processing a media item.</summary>
    public const string PipelineStepCompleted = "media.pipeline.step.completed";

    /// <summary>
    /// NATS request/reply subject for reverse geocoding.
    /// The requester sends a <see cref="ReverseGeocodingRequest"/> and expects a
    /// <see cref="ReverseGeocodingReply"/> in return.
    /// </summary>
    public const string ReverseGeocodingRequest = "geocoding.reverse";
}
