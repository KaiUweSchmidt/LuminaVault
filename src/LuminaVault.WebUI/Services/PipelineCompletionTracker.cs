using System.Collections.Concurrent;
using LuminaVault.ServiceDefaults;
using NATS.Client.Core;

namespace LuminaVault.WebUI.Services;

/// <summary>
/// Subscribes to <see cref="NatsSubjects.PipelineStepCompleted"/> events and tracks
/// end-to-end pipeline durations per <c>MediaId</c>. Used by <see cref="BatchImportService"/>
/// to compute accurate average import times even when pipeline steps are asynchronous.
/// </summary>
public sealed class PipelineCompletionTracker : IHostedService, IDisposable
{
    private readonly INatsConnection _nats;
    private readonly ILogger<PipelineCompletionTracker> _logger;
    private CancellationTokenSource? _cts;
    private Task? _subscribeTask;

    /// <summary>
    /// Stores the total pipeline duration (ms) per completed <c>MediaId</c>.
    /// Only MediaIds that were registered via <see cref="Track"/> are counted.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, long> _completedDurations = new();

    /// <summary>MediaIds expected by the current batch. Events for unknown ids are ignored.</summary>
    private readonly ConcurrentDictionary<Guid, byte> _expectedIds = new();

    public PipelineCompletionTracker(INatsConnection nats, ILogger<PipelineCompletionTracker> logger)
    {
        _nats = nats;
        _logger = logger;
    }

    /// <summary>Registers a <c>MediaId</c> so that its completion event will be tracked.</summary>
    public void Track(Guid mediaId) => _expectedIds.TryAdd(mediaId, 0);

    /// <summary>Returns the total pipeline duration in ms for the given media, or <c>null</c> if not yet complete.</summary>
    public long? GetTotalDuration(Guid mediaId) =>
        _completedDurations.TryGetValue(mediaId, out var ms) ? ms : null;

    /// <summary>Returns the average pipeline duration across all tracked completions, or <c>null</c> if none.</summary>
    public TimeSpan? AveragePipelineDuration =>
        _completedDurations.IsEmpty
            ? null
            : TimeSpan.FromMilliseconds(_completedDurations.Values.Average());

    /// <summary>Number of media items that have completed the full pipeline.</summary>
    public int CompletedCount => _completedDurations.Count;

    /// <summary>Raised when a <see cref="PipelineSteps.PipelineComplete"/> event is received.</summary>
    public event Action<Guid, long>? OnPipelineCompleted;

    /// <summary>Clears all tracked completion data and expected ids. Call when starting a new batch.</summary>
    public void Clear()
    {
        _completedDurations.Clear();
        _expectedIds.Clear();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _subscribeTask = SubscribeLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
        }

        if (_subscribeTask is not null)
        {
            try
            {
                await _subscribeTask;
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown
            }
        }
    }

    private async Task SubscribeLoopAsync(CancellationToken ct)
    {
        _logger.LogInformation("[PipelineTracker] Subscribing to {Subject}", NatsSubjects.PipelineStepCompleted);

        try
        {
            await foreach (var msg in _nats.SubscribeAsync<PipelineStepCompletedEvent>(
                NatsSubjects.PipelineStepCompleted, cancellationToken: ct))
            {
                if (msg.Data is not { } evt)
                    continue;

                if (evt.StepName == PipelineSteps.PipelineComplete
                    && _expectedIds.ContainsKey(evt.MediaId))
                {
                    _completedDurations[evt.MediaId] = evt.DurationMs;
                    _logger.LogInformation(
                        "[PipelineTracker] Pipeline abgeschlossen: MediaId={MediaId}, Gesamtdauer={DurationMs}ms",
                        evt.MediaId, evt.DurationMs);
                    OnPipelineCompleted?.Invoke(evt.MediaId, evt.DurationMs);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected on shutdown
        }
    }

    public void Dispose()
    {
        _cts?.Dispose();
    }
}
