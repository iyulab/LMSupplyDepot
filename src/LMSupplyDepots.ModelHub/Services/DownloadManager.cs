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
    private readonly ConcurrentDictionary<string, DownloadInfo> _downloadInfos = new();
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
    /// Gets information about all current downloads
    /// </summary>
    public async Task<IEnumerable<DownloadInfo>> GetAllDownloadsAsync(CancellationToken cancellationToken = default)
    {
        var allDownloads = new List<DownloadInfo>();

        // Add active downloads
        foreach (var kvp in _downloadInfos)
        {
            var downloadInfo = kvp.Value;

            // Update current progress
            var currentProgress = GetDownloadProgress(kvp.Key);
            if (currentProgress != null)
            {
                downloadInfo.Progress = currentProgress;
            }

            allDownloads.Add(downloadInfo);
        }

        // Check for paused downloads from file system
        await AddPausedDownloadsAsync(allDownloads, cancellationToken);

        return allDownloads.OrderByDescending(d => d.StartedAt ?? DateTime.MinValue);
    }

    /// <summary>
    /// Adds paused downloads from file system to the list
    /// </summary>
    private async Task AddPausedDownloadsAsync(List<DownloadInfo> allDownloads, CancellationToken cancellationToken)
    {
        try
        {
            var downloadsDir = Path.Combine(_options.DataPath, ".downloads");
            if (!Directory.Exists(downloadsDir))
                return;

            var statusFiles = Directory.GetFiles(downloadsDir, "*.download");

            foreach (var statusFile in statusFiles)
            {
                var fileName = Path.GetFileNameWithoutExtension(statusFile);

                // Skip if already in active downloads
                if (_downloadInfos.ContainsKey(fileName))
                    continue;

                var downloadInfo = new DownloadInfo
                {
                    ModelId = fileName,
                    Status = ModelDownloadStatus.Paused,
                    Progress = GetDownloadProgress(fileName),
                    StartedAt = File.GetCreationTime(statusFile)
                };

                allDownloads.Add(downloadInfo);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error retrieving paused downloads from file system");
        }
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

        // Create download info
        var downloadInfo = new DownloadInfo
        {
            ModelId = sourceId,
            Status = ModelDownloadStatus.Downloading,
            StartedAt = DateTime.UtcNow
        };

        _downloadInfos[sourceId] = downloadInfo;

        await _downloadSemaphore.WaitAsync(cancellationToken);
        try
        {
            // Create progress wrapper to update our tracking
            var progressWrapper = progress != null ? new Progress<ModelDownloadProgress>(p =>
            {
                downloadInfo.Progress = p;
                progress.Report(p);
            }) : new Progress<ModelDownloadProgress>(p => downloadInfo.Progress = p);

            var result = await downloader.DownloadModelAsync(sourceId, targetDirectory, progressWrapper, cts.Token);

            // Update status to completed
            downloadInfo.Status = ModelDownloadStatus.Completed;
            downloadInfo.ModelInfo = result;

            return result;
        }
        catch (OperationCanceledException)
        {
            downloadInfo.Status = ModelDownloadStatus.Cancelled;
            throw;
        }
        catch (Exception)
        {
            downloadInfo.Status = ModelDownloadStatus.Failed;
            throw;
        }
        finally
        {
            _downloadSemaphore.Release();
            _activeDownloads.TryRemove(sourceId, out _);

            // Remove from tracking after a delay for completed/failed downloads
            if (downloadInfo.Status == ModelDownloadStatus.Completed ||
                downloadInfo.Status == ModelDownloadStatus.Failed ||
                downloadInfo.Status == ModelDownloadStatus.Cancelled)
            {
                _ = Task.Delay(TimeSpan.FromMinutes(5)).ContinueWith(task =>
                    _downloadInfos.TryRemove(sourceId, out _));
            }

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

            // Update download info
            if (_downloadInfos.TryGetValue(sourceId, out var downloadInfo))
            {
                downloadInfo.Status = ModelDownloadStatus.Paused;
            }
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

        // Update download info
        if (_downloadInfos.TryGetValue(sourceId, out var downloadInfo))
        {
            downloadInfo.Status = ModelDownloadStatus.Cancelled;
        }

        return cancelled;
    }

    /// <summary>
    /// Gets download status by checking file system and active downloads
    /// </summary>
    public ModelDownloadStatus? GetDownloadStatus(string sourceId)
    {
        // Check active downloads first
        if (_downloadInfos.TryGetValue(sourceId, out var downloadInfo))
        {
            return downloadInfo.Status;
        }

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
        // Check if we have cached progress first
        if (_downloadInfos.TryGetValue(sourceId, out var downloadInfo) && downloadInfo.Progress != null)
        {
            return downloadInfo.Progress;
        }

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
            _downloadInfos.Clear();
            _downloadSemaphore.Dispose();
            _disposed = true;
        }
    }
}