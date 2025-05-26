namespace LMSupplyDepots.ModelHub.Services;

/// <summary>
/// Manages model download operations using file system for state tracking
/// </summary>
public class DownloadManager : IDisposable
{
    private readonly ILogger<DownloadManager> _logger;
    private readonly ModelHubOptions _options;
    private readonly IEnumerable<IModelDownloader> _downloaders;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeDownloads = new();
    private readonly SemaphoreSlim _downloadSemaphore;
    private bool _disposed;

    public DownloadManager(
        ILogger<DownloadManager> logger,
        IOptions<ModelHubOptions> options,
        IEnumerable<IModelDownloader> downloaders)
    {
        _logger = logger;
        _options = options.Value;
        _downloaders = downloaders;
        _downloadSemaphore = new SemaphoreSlim(_options.MaxConcurrentDownloads);
    }

    /// <summary>
    /// Starts download for a model
    /// </summary>
    public async Task<LMModel> DownloadModelAsync(
        string sourceId,
        string targetDirectory,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_activeDownloads.ContainsKey(sourceId))
        {
            throw new InvalidOperationException($"Download already in progress for {sourceId}");
        }

        var downloader = GetDownloader(sourceId);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _activeDownloads[sourceId] = cts;

        await _downloadSemaphore.WaitAsync(cancellationToken);
        try
        {
            return await downloader.DownloadModelAsync(sourceId, targetDirectory, progress, cts.Token);
        }
        finally
        {
            _downloadSemaphore.Release();
            _activeDownloads.TryRemove(sourceId, out _);
            cts.Dispose();
        }
    }

    /// <summary>
    /// Pauses an active download
    /// </summary>
    public async Task<bool> PauseDownloadAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        if (!_activeDownloads.TryGetValue(sourceId, out var cts))
        {
            return false;
        }

        var downloader = GetDownloader(sourceId);
        var paused = await downloader.PauseDownloadAsync(sourceId, cancellationToken);

        if (paused)
        {
            cts.Cancel();
        }

        return paused;
    }

    /// <summary>
    /// Cancels an active download
    /// </summary>
    public async Task<bool> CancelDownloadAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        var downloader = GetDownloader(sourceId);
        var cancelled = await downloader.CancelDownloadAsync(sourceId, cancellationToken);

        if (_activeDownloads.TryRemove(sourceId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }

        return cancelled;
    }

    /// <summary>
    /// Gets download status by checking file system and active downloads
    /// </summary>
    public ModelDownloadStatus? GetDownloadStatus(string sourceId)
    {
        if (_activeDownloads.ContainsKey(sourceId))
        {
            return ModelDownloadStatus.Downloading;
        }

        return DownloadStatusHelper.IsPaused(sourceId, _options.DataPath)
            ? ModelDownloadStatus.Paused
            : null;
    }

    /// <summary>
    /// Gets download progress by checking file sizes
    /// </summary>
    public ModelDownloadProgress? GetDownloadProgress(string sourceId)
    {
        var totalSize = DownloadStatusHelper.GetTotalSize(sourceId, _options.DataPath);
        if (!totalSize.HasValue)
        {
            return null;
        }

        var downloadedSize = CalculateDownloadedSize(sourceId);
        var status = _activeDownloads.ContainsKey(sourceId)
            ? ModelDownloadStatus.Downloading
            : ModelDownloadStatus.Paused;

        return new ModelDownloadProgress
        {
            ModelId = sourceId,
            FileName = sourceId.GetFileNameFromSourceId(),
            BytesDownloaded = downloadedSize,
            TotalBytes = totalSize.Value,
            BytesPerSecond = 0,
            Status = status
        };
    }

    private long CalculateDownloadedSize(string sourceId)
    {
        if (!ModelIdentifier.TryParse(sourceId, out var identifier))
        {
            return 0;
        }

        var modelDir = FileSystemHelper.GetModelDirectoryPath(identifier, _options.DataPath);
        return modelDir.GetModelFilesSize();
    }

    private IModelDownloader GetDownloader(string sourceId)
    {
        return _downloaders.FirstOrDefault(d => d.CanHandle(sourceId))
            ?? throw new ModelSourceNotFoundException(sourceId);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            foreach (var cts in _activeDownloads.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }
            _activeDownloads.Clear();
            _downloadSemaphore.Dispose();
            _disposed = true;
        }
    }
}