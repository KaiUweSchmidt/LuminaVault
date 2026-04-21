namespace LuminaVault.ServiceDefaults;

/// <summary>
/// Event published via NATS when a pipeline step finishes processing a media item.
/// The <see cref="PipelineCompletionTracker"/> (WebUI) aggregates these to compute
/// accurate end-to-end import durations even when steps run asynchronously.
/// </summary>
public sealed record PipelineStepCompletedEvent(
    Guid MediaId,
    string StepName,
    long DurationMs,
    bool Success,
    DateTimeOffset CompletedAt);
