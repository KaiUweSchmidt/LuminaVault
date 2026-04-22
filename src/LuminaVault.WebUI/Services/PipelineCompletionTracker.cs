using System.Collections.Concurrent;
using LuminaVault.ServiceDefaults;
using NATS.Client.Core;

namespace LuminaVault.WebUI.Services;

/// <summary>
/// Subscribes to <see cref="NatsSubjects.PipelineStepCompleted"/> events and tracks
/// end-to-end pipeline durations per <c>MediaId</c>. Used by <see cref="BatchImportService"/>
/// to compute accurate average import times even when pipeline steps are asynchronous.
/// A media item is considered complete when both ThumbnailGeneration and ObjectRecognition
/// steps have been received (or only ThumbnailGeneration for non-image media).
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

    /// <summary>Running sum of all completed pipeline durations for O(1) average.</summary>
    private long _completedDurationSum;
    private int _completedCount;

    /// <summary>MediaIds expected by the current batch. Events for unknown ids are ignored.</summary>
    private readonly ConcurrentDictionary<Guid, byte> _expectedIds = new();

    /// <summary>Tracks which steps have completed per MediaId and their cumulative duration.</summary>
    private readonly ConcurrentDictionary<Guid, StepProgress> _stepProgress = new();

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
        _completedCount > 0
            ? TimeSpan.FromMilliseconds((double)Interlocked.Read(ref _completedDurationSum) / _completedCount)
            : null;

    /// <summary>Number of media items that have completed the full pipeline.</summary>
    public int CompletedCount => _completedCount;

    /// <summary>Raised when all expected pipeline steps for a media item have completed.</summary>
    public event Action<Guid, long>? OnPipelineCompleted;

    /// <summary>Clears all tracked completion data and expected ids. Call when starting a new batch.</summary>
    public void Clear()
    {
        _completedDurations.Clear();
        _expectedIds.Clear();
        _stepProgress.Clear();
        _completedDurationSum = 0;
        _completedCount = 0;
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

                if (!_expectedIds.ContainsKey(evt.MediaId))
                    continue;

                // Already completed — skip duplicate events
                if (_completedDurations.ContainsKey(evt.MediaId))
                    continue;

                var progress = _stepProgress.GetOrAdd(evt.MediaId, _ => new StepProgress());
                progress.Record(evt.StepName, evt.DurationMs);

                _logger.LogInformation(
                    "[PipelineTracker] Step empfangen: MediaId={MediaId}, Step={Step}, Duration={DurationMs}ms",
                    evt.MediaId, evt.StepName, evt.DurationMs);

                if (progress.IsComplete)
                {
                    var totalMs = progress.TotalDurationMs;
                    _completedDurations[evt.MediaId] = totalMs;
                    Interlocked.Add(ref _completedDurationSum, totalMs);
                    Interlocked.Increment(ref _completedCount);
                    _stepProgress.TryRemove(evt.MediaId, out _);
                    _logger.LogInformation(
                        "[PipelineTracker] Pipeline abgeschlossen: MediaId={MediaId}, Gesamtdauer={DurationMs}ms",
                        evt.MediaId, totalMs);
                    OnPipelineCompleted?.Invoke(evt.MediaId, totalMs);
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

    /// <summary>Tracks step completions for a single media item.</summary>
    private sealed class StepProgress
    {
        private readonly HashSet<string> _completedSteps = [];
        private long _totalDurationMs;

        private static readonly HashSet<string> RequiredSteps =
        [
            PipelineSteps.ThumbnailGeneration,
            PipelineSteps.ObjectRecognition,
            PipelineSteps.FaceRecognition,
            PipelineSteps.Geocoding
        ];

        public void Record(string stepName, long durationMs)
        {
            if (_completedSteps.Add(stepName))
                Interlocked.Add(ref _totalDurationMs, durationMs);
        }

        public bool IsComplete => RequiredSteps.IsSubsetOf(_completedSteps);

        public long TotalDurationMs => Interlocked.Read(ref _totalDurationMs);
    }
}
