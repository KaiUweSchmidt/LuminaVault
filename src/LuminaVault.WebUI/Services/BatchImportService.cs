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

    private CancellationTokenSource? _cts;
    private Task? _importTask;

    // ── Public state ──────────────────────────────────────────────────────────

    public BatchImportStatus Status { get; private set; } = BatchImportStatus.Idle;
    public string? DirectoryPath { get; private set; }
    public IReadOnlyList<string> FilesToImport { get; private set; } = [];
    public int TotalFiles => FilesToImport.Count;
    public int ImportedCount { get; private set; }
    public int ErrorCount { get; private set; }
    public string? CurrentFile { get; private set; }
    public string? OwnerCircuitId { get; private set; }

    private readonly List<double> _importMilliseconds = [];

    /// <summary>Average HTTP upload duration (time until the server responds).</summary>
    public TimeSpan? AverageUploadTime =>
        _importMilliseconds.Count > 0
            ? TimeSpan.FromMilliseconds(_importMilliseconds.Average())
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
        ImportedCount = 0;
        ErrorCount = 0;
        _importMilliseconds.Clear();
        CurrentFile = null;

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
        ImportedCount = 0;
        ErrorCount = 0;
        _importMilliseconds.Clear();
        pipelineTracker.Clear();
        CurrentFile = null;
        OwnerCircuitId = null;
        NotifyProgress();
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private async Task RunLoopAsync(CancellationToken ct)
    {
        var startIndex = ImportedCount + ErrorCount;

        for (int i = startIndex; i < FilesToImport.Count; i++)
        {
            if (ct.IsCancellationRequested)
                break;

            var filePath = FilesToImport[i];
            CurrentFile = Path.GetFileName(filePath);
            NotifyProgress();

            var contentType = ResolveContentType(filePath);
            if (contentType is null)
            {
                ErrorCount++;
                NotifyProgress();
                continue;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var scope = scopeFactory.CreateScope();
                var mediaApi = scope.ServiceProvider.GetRequiredService<MediaApiClient>();

                await using var stream = File.OpenRead(filePath);
                var fileName = Path.GetFileName(filePath);
                var title = Path.GetFileNameWithoutExtension(filePath);

                // Pre-generate the MediaId and register it with the tracker BEFORE the
                // upload so that the NATS PipelineComplete event (published at the end of
                // the import endpoint) is not missed due to a race condition.
                var mediaId = Guid.NewGuid();
                pipelineTracker.Track(mediaId);

                var result = await mediaApi.UploadMediaAsync(stream, fileName, contentType, title, ct, mediaId);

                sw.Stop();
                lock (_importMilliseconds)
                    _importMilliseconds.Add(sw.Elapsed.TotalMilliseconds);
                ImportedCount++;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[BatchImport] Failed to import: {FilePath}", filePath);
                ErrorCount++;
            }

            NotifyProgress();
        }

        if (!ct.IsCancellationRequested && Status == BatchImportStatus.Running)
        {
            Status = BatchImportStatus.Completed;
            CurrentFile = null;
        }

        NotifyProgress();
    }

    private static List<string> ScanDirectory(string path)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Verzeichnis nicht gefunden: {path}");

        return [.. Directory
            .EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
            .Where(f => ValidExtensions.Contains(
                Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .OrderBy(f => f)];
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
        try { OnProgressChanged?.Invoke(); }
        catch (Exception ex) { logger.LogDebug(ex, "[BatchImport] Progress notification handler threw an exception."); }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
