namespace LuminaVault.WebUI.Services;

/// <summary>
/// Represents the current status of a batch import operation.
/// </summary>
public enum BatchImportStatus
{
    Idle,
    Scanning,
    Running,
    Paused,
    Completed,
    Cancelled
}

/// <summary>
/// Singleton service that manages a directory batch import operation.
/// The import continues even when the user navigates away from the import page.
/// The import is paused when the owning browser circuit closes (tab/window closed).
/// </summary>
public sealed class BatchImportService(
    IServiceScopeFactory scopeFactory,
    PipelineCompletionTracker pipelineTracker,
    ILogger<BatchImportService> logger) : IDisposable
{
    private static readonly string[] ValidExtensions =
    [
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".tiff", ".tif",
        ".mp4", ".mov", ".avi", ".mkv", ".wmv", ".m4v", ".webm"
    ];

    /// <summary>Number of concurrent HTTP uploads to the import service.</summary>
    private const int UploadConcurrency = 4;

    /// <summary>Minimum interval between UI progress notifications to avoid flooding Blazor.</summary>
    private static readonly TimeSpan ProgressThrottle = TimeSpan.FromMilliseconds(500);

    private CancellationTokenSource? _cts;
    private Task? _importTask;
    private DateTime _lastProgressNotification;

    // ── Public state ──────────────────────────────────────────────────────────

    public BatchImportStatus Status { get; private set; } = BatchImportStatus.Idle;
    public string? DirectoryPath { get; private set; }
    public IReadOnlyList<string> FilesToImport { get; private set; } = [];
    public int TotalFiles => FilesToImport.Count;

    private int _importedCountBacking;
    private int _errorCountBacking;
    private long _importMillisecondsSumBacking;
    private int _importMillisecondsCountBacking;

    public int ImportedCount => _importedCountBacking;
    public int ErrorCount => _errorCountBacking;
    public string? CurrentFile { get; private set; }
    public string? OwnerCircuitId { get; private set; }

    /// <summary>Average HTTP upload duration (time until the server responds).</summary>
    public TimeSpan? AverageUploadTime =>
        _importMillisecondsCountBacking > 0
            ? TimeSpan.FromMilliseconds((double)_importMillisecondsSumBacking / _importMillisecondsCountBacking)
            : null;

    /// <summary>
    /// Average end-to-end pipeline duration including all async steps
    /// (thumbnail generation, object recognition, etc.) tracked via NATS completion events.
    /// Falls back to <see cref="AverageUploadTime"/> when no pipeline completions are available yet.
    /// </summary>
    public TimeSpan? AverageImportTime =>
        pipelineTracker.AveragePipelineDuration ?? AverageUploadTime;

    /// <summary>Number of media items that have fully completed the async pipeline.</summary>
    public int PipelineCompletedCount => pipelineTracker.CompletedCount;

    public TimeSpan? EstimatedTimeRemaining
    {
        get
        {
            if (AverageImportTime is null || Status != BatchImportStatus.Running)
                return null;
            var remaining = TotalFiles - ImportedCount - ErrorCount;
            return TimeSpan.FromMilliseconds(AverageImportTime.Value.TotalMilliseconds * remaining);
        }
    }

    public double ProgressPercent =>
        TotalFiles > 0 ? (double)(ImportedCount + ErrorCount) / TotalFiles * 100 : 0;

    /// <summary>Raised on the thread pool whenever import state changes.</summary>
    public event Action? OnProgressChanged;

    // ── Circuit ownership ─────────────────────────────────────────────────────

    public void SetOwner(string? circuitId) => OwnerCircuitId = circuitId;

    // ── Control ───────────────────────────────────────────────────────────────

    /// <summary>Scans the given directory and starts importing.</summary>
    public Task StartAsync(string directoryPath)
    {
        if (Status is BatchImportStatus.Running or BatchImportStatus.Scanning)
            throw new InvalidOperationException("A batch import is already running.");

        DirectoryPath = directoryPath;
        Status = BatchImportStatus.Scanning;
        NotifyProgress();

        FilesToImport = ScanDirectory(directoryPath);
        _importedCountBacking = 0;
        _errorCountBacking = 0;
        _importMillisecondsSumBacking = 0;
        _importMillisecondsCountBacking = 0;
        CurrentFile = null;
        _lastProgressNotification = DateTime.MinValue;

        Status = BatchImportStatus.Running;
        NotifyProgress();

        _cts = new CancellationTokenSource();
        _importTask = RunLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    /// <summary>Resumes a paused import.</summary>
    public Task ResumeAsync()
    {
        if (Status != BatchImportStatus.Paused)
            return Task.CompletedTask;

        Status = BatchImportStatus.Running;
        NotifyProgress();

        _cts = new CancellationTokenSource();
        _importTask = RunLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    /// <summary>Pauses the running import.</summary>
    public void Pause()
    {
        if (Status != BatchImportStatus.Running)
            return;
        Status = BatchImportStatus.Paused;
        _cts?.Cancel();
        NotifyProgress();
    }

    /// <summary>Cancels and resets the import.</summary>
    public void Cancel()
    {
        _cts?.Cancel();
        Status = BatchImportStatus.Cancelled;
        CurrentFile = null;
        NotifyProgress();
    }

    /// <summary>Resets service to idle state.</summary>
    public void Reset()
    {
        _cts?.Cancel();
        Status = BatchImportStatus.Idle;
        DirectoryPath = null;
        FilesToImport = [];
        _importedCountBacking = 0;
        _errorCountBacking = 0;
        _importMillisecondsSumBacking = 0;
        _importMillisecondsCountBacking = 0;
        pipelineTracker.Clear();
        CurrentFile = null;
        OwnerCircuitId = null;
        NotifyProgress();
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private async Task RunLoopAsync(CancellationToken ct)
    {
        var startIndex = ImportedCount + ErrorCount;
        var semaphore = new SemaphoreSlim(UploadConcurrency, UploadConcurrency);
        var tasks = new List<Task>();

        for (int i = startIndex; i < FilesToImport.Count; i++)
        {
            if (ct.IsCancellationRequested)
                break;

            var filePath = FilesToImport[i];
            var contentType = ResolveContentType(filePath);
            if (contentType is null)
            {
                Interlocked.Increment(ref _errorCountBacking);
                ThrottledNotifyProgress();
                continue;
            }

            await semaphore.WaitAsync(ct);
            if (ct.IsCancellationRequested)
                break;

            tasks.Add(UploadOneAsync(filePath, contentType, semaphore, ct));

            // Prune completed tasks periodically to avoid unbounded list growth
            if (tasks.Count >= UploadConcurrency * 4)
                tasks.RemoveAll(t => t.IsCompleted);
        }

        // Wait for all in-flight uploads to finish
        try { await Task.WhenAll(tasks); }
        catch (OperationCanceledException) { /* expected */ }

        if (!ct.IsCancellationRequested && Status == BatchImportStatus.Running)
        {
            Status = BatchImportStatus.Completed;
            CurrentFile = null;
        }

        // Always fire a final notification so the UI shows 100%
        NotifyProgress();
    }

    private async Task UploadOneAsync(string filePath, string contentType, SemaphoreSlim semaphore, CancellationToken ct)
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var scope = scopeFactory.CreateScope();
                var mediaApi = scope.ServiceProvider.GetRequiredService<MediaApiClient>();

                await using var stream = File.OpenRead(filePath);
                var fileName = Path.GetFileName(filePath);
                var title = Path.GetFileNameWithoutExtension(filePath);

                var mediaId = Guid.NewGuid();
                pipelineTracker.Track(mediaId);

                await mediaApi.UploadMediaAsync(stream, fileName, contentType, title, ct, mediaId);

                sw.Stop();
                Interlocked.Add(ref _importMillisecondsSumBacking, (long)sw.Elapsed.TotalMilliseconds);
                Interlocked.Increment(ref _importMillisecondsCountBacking);
                Interlocked.Increment(ref _importedCountBacking);
            }
            catch (OperationCanceledException)
            {
                // Let the main loop handle cancellation
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[BatchImport] Failed to import: {FilePath}", filePath);
                Interlocked.Increment(ref _errorCountBacking);
            }

            ThrottledNotifyProgress();
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static List<string> ScanDirectory(string path)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Verzeichnis nicht gefunden: {path}");

        return [.. Directory
            .EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
            .Where(f => ValidExtensions.Contains(
                Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))];
    }

    private static string? ResolveContentType(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png"            => "image/png",
            ".gif"            => "image/gif",
            ".webp"           => "image/webp",
            ".bmp"            => "image/bmp",
            ".tiff" or ".tif" => "image/tiff",
            ".mp4"            => "video/mp4",
            ".mov"            => "video/quicktime",
            ".avi"            => "video/x-msvideo",
            ".mkv"            => "video/x-matroska",
            ".wmv"            => "video/x-ms-wmv",
            ".m4v"            => "video/x-m4v",
            ".webm"           => "video/webm",
            _                 => null
        };
    }

    private void NotifyProgress()
    {
        _lastProgressNotification = DateTime.UtcNow;
        try { OnProgressChanged?.Invoke(); }
        catch (Exception ex) { logger.LogDebug(ex, "[BatchImport] Progress notification handler threw an exception."); }
    }

    private void ThrottledNotifyProgress()
    {
        if (DateTime.UtcNow - _lastProgressNotification >= ProgressThrottle)
            NotifyProgress();
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
