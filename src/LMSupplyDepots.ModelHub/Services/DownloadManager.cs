namespace LMSupplyDepots.ModelHub.Services;

/// <summary>
/// Simple download manager with basic cancellation and state tracking
/// </summary>
public sealed class DownloadManager : IDisposable
{
    private readonly ILogger<DownloadManager> _logger;
    private readonly IEnumerable<IModelDownloader> _downloaders;
    private readonly SemaphoreSlim _downloadSemaphore;
    private readonly string _modelsPath;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeCancellations = new();
    private bool _disposed;

    public DownloadManager(
        ILogger<DownloadManager> logger,
        IEnumerable<IModelDownloader> downloaders,
        IOptions<ModelHubOptions> options)
    {
        _logger = logger;
        _downloaders = downloaders;
        _modelsPath = options.Value.ModelsDirectory;
        _downloadSemaphore = new SemaphoreSlim(options.Value.MaxConcurrentDownloads);
    }

    /// <summary>
    /// Downloads a model with basic cancellation support
    /// </summary>
    public async Task<LMModel> DownloadModelAsync(
        string sourceId,
        string targetDirectory,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_activeCancellations.ContainsKey(sourceId))
        {
            throw new InvalidOperationException($"Download already active for {sourceId}");
        }

        var downloader = GetDownloader(sourceId);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _activeCancellations[sourceId] = cts;

        await _downloadSemaphore.WaitAsync(cancellationToken);
        try
        {
            var result = await downloader.DownloadModelAsync(sourceId, targetDirectory, progress, cts.Token);
            return result;
        }
        finally
        {
            _downloadSemaphore.Release();
            _activeCancellations.TryRemove(sourceId, out _);
        }
    }

    /// <summary>
    /// Pauses a download by cancelling the operation
    /// </summary>
    public Task<bool> PauseDownloadAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        if (_activeCancellations.TryRemove(sourceId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    /// <summary>
    /// Resumes a paused download
    /// </summary>
    public async Task<LMModel> ResumeDownloadAsync(
        string sourceId,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_activeCancellations.ContainsKey(sourceId))
        {
            throw new InvalidOperationException($"Download already active for {sourceId}");
        }

        var downloader = GetDownloader(sourceId);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _activeCancellations[sourceId] = cts;

        await _downloadSemaphore.WaitAsync(cancellationToken);
        try
        {
            var result = await downloader.ResumeDownloadAsync(sourceId, progress, cts.Token);
            return result;
        }
        finally
        {
            _downloadSemaphore.Release();
            _activeCancellations.TryRemove(sourceId, out _);
        }
    }

    /// <summary>
    /// Cancels a download with cleanup
    /// </summary>
    public async Task<bool> CancelDownloadAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        var cancelled = false;
        if (_activeCancellations.TryRemove(sourceId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
            cancelled = true;
        }

        var downloader = GetDownloader(sourceId);
        await downloader.CancelDownloadAsync(sourceId, cancellationToken);
        return cancelled;
    }

    /// <summary>
    /// Gets download status
    /// </summary>
    public ModelDownloadStatus? GetDownloadStatus(string sourceId)
    {
        if (_activeCancellations.ContainsKey(sourceId))
        {
            return ModelDownloadStatus.Downloading;
        }

        var downloader = GetDownloader(sourceId);
        var task = downloader.GetDownloadStatusAsync(sourceId, CancellationToken.None);

        if (task.Wait(TimeSpan.FromSeconds(1)) && task.IsCompletedSuccessfully)
        {
            return task.Result;
        }

        return null;
    }

    /// <summary>
    /// Gets download progress from state files
    /// </summary>
    public ModelDownloadProgress? GetDownloadProgress(string sourceId)
    {
        var progress = DownloadStateHelper.GetRealTimeProgress(sourceId, _modelsPath);

        if (progress != null && _activeCancellations.ContainsKey(sourceId))
        {
            return progress.WithStatus(ModelDownloadStatus.Downloading);
        }

        return progress;
    }

    /// <summary>
    /// Gets all downloads
    /// </summary>
    public Task<IEnumerable<DownloadInfo>> GetAllDownloadsAsync(CancellationToken cancellationToken = default)
    {
        var downloads = new List<DownloadInfo>();

        foreach (var modelId in _activeCancellations.Keys)
        {
            var progress = GetDownloadProgress(modelId);
            downloads.Add(new DownloadInfo
            {
                ModelId = modelId,
                Status = ModelDownloadStatus.Downloading,
                Progress = progress,
                StartedAt = progress?.StartedAt
            });
        }

        var states = DownloadStateHelper.FindDownloadStatesInModelsPath("", _modelsPath);
        foreach (var state in states)
        {
            if (!_activeCancellations.ContainsKey(state.ModelId))
            {
                var progress = DownloadStateHelper.GetRealTimeProgress(state.ModelId, _modelsPath);
                downloads.Add(new DownloadInfo
                {
                    ModelId = state.ModelId,
                    Status = ModelDownloadStatus.Paused,
                    Progress = progress,
                    StartedAt = state.StartedAt
                });
            }
        }

        return Task.FromResult<IEnumerable<DownloadInfo>>(downloads);
    }

    private IModelDownloader GetDownloader(string sourceId)
    {
        return _downloaders.FirstOrDefault(d => d.CanHandle(sourceId))
            ?? throw new ModelSourceNotFoundException(sourceId);
    }

    public void Dispose()
    {
        if (_disposed) return;

        foreach (var cts in _activeCancellations.Values)
        {
            try
            {
                cts.Cancel();
                cts.Dispose();
            }
            catch { }
        }
        _activeCancellations.Clear();
        _downloadSemaphore.Dispose();
        _disposed = true;
    }
}