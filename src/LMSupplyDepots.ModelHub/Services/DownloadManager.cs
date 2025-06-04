namespace LMSupplyDepots.ModelHub.Services;

/// <summary>
/// Enhanced download manager with unified state management and improved concurrency control
/// </summary>
public class DownloadManager : IDisposable
{
    private readonly ILogger<DownloadManager> _logger;
    private readonly ModelHubOptions _options;
    private readonly IEnumerable<IModelDownloader> _downloaders;
    private readonly ConcurrentDictionary<string, DownloadSession> _activeSessions = new();
    private readonly SemaphoreSlim _downloadSemaphore;
    private readonly Timer _statusUpdateTimer;
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

        // Status validation timer every 5 seconds
        _statusUpdateTimer = new Timer(ValidateDownloadStatuses, null,
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Download session with thread-safe state management
    /// </summary>
    internal class DownloadSession
    {
        private readonly object _lock = new();
        private ModelDownloadStatus _status;
        private ModelDownloadProgress? _lastProgress;

        public string ModelId { get; init; } = string.Empty;
        public CancellationTokenSource CancellationTokenSource { get; init; } = new();
        public DateTime StartTime { get; init; } = DateTime.UtcNow;
        public Task? DownloadTask { get; set; }

        public ModelDownloadStatus Status
        {
            get { lock (_lock) return _status; }
            set { lock (_lock) _status = value; }
        }

        public ModelDownloadProgress? LastProgress
        {
            get { lock (_lock) return _lastProgress; }
            set { lock (_lock) _lastProgress = value; }
        }

        public void UpdateProgress(ModelDownloadProgress progress)
        {
            lock (_lock)
            {
                _lastProgress = progress;
                // Sync status from progress
                if (progress.Status != ModelDownloadStatus.Downloading && progress.Status != ModelDownloadStatus.Initializing)
                {
                    _status = progress.Status;
                }
            }
        }

        public bool TrySetStatus(ModelDownloadStatus expectedStatus, ModelDownloadStatus newStatus)
        {
            lock (_lock)
            {
                if (_status == expectedStatus)
                {
                    _status = newStatus;
                    return true;
                }
                return false;
            }
        }

        public void ForceSetStatus(ModelDownloadStatus newStatus)
        {
            lock (_lock)
            {
                _status = newStatus;
            }
        }
    }

    /// <summary>
    /// Starts or resumes a model download with enhanced state tracking
    /// </summary>
    public async Task<LMModel> DownloadModelAsync(
        string sourceId,
        string targetDirectory,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Check for existing session
        if (_activeSessions.TryGetValue(sourceId, out var existingSession))
        {
            if (existingSession.Status == ModelDownloadStatus.Downloading)
            {
                throw new InvalidOperationException($"Download already in progress for {sourceId}");
            }
        }

        var downloader = GetDownloader(sourceId);
        var session = new DownloadSession
        {
            ModelId = sourceId,
        };
        session.Status = ModelDownloadStatus.Initializing;

        // Atomic session registration
        if (!_activeSessions.TryAdd(sourceId, session))
        {
            // Another thread added a session, check its status
            if (_activeSessions.TryGetValue(sourceId, out var conflictSession) &&
                conflictSession.Status == ModelDownloadStatus.Downloading)
            {
                throw new InvalidOperationException($"Download already in progress for {sourceId}");
            }
            // Replace with our session
            _activeSessions[sourceId] = session;
        }

        await _downloadSemaphore.WaitAsync(cancellationToken);
        try
        {
            session.Status = ModelDownloadStatus.Downloading;

            var progressWrapper = new Progress<ModelDownloadProgress>(p =>
            {
                session.UpdateProgress(p);
                progress?.Report(p);
            });

            // Create download task
            var downloadTask = downloader.DownloadModelAsync(
                sourceId, targetDirectory, progressWrapper, session.CancellationTokenSource.Token);

            session.DownloadTask = downloadTask;
            var result = await downloadTask;

            session.Status = ModelDownloadStatus.Completed;

            // Schedule session cleanup
            _ = Task.Delay(TimeSpan.FromMinutes(5)).ContinueWith(_ =>
            {
                if (_activeSessions.TryRemove(sourceId, out var removedSession))
                {
                    removedSession.CancellationTokenSource.Dispose();
                }
            });

            return result;
        }
        catch (OperationCanceledException)
        {
            session.Status = ModelDownloadStatus.Cancelled;
            throw;
        }
        catch (Exception)
        {
            session.Status = ModelDownloadStatus.Failed;
            throw;
        }
        finally
        {
            _downloadSemaphore.Release();
        }
    }

    /// <summary>
    /// Pauses an active download with proper state validation
    /// </summary>
    public async Task<bool> PauseDownloadAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        if (_activeSessions.TryGetValue(sourceId, out var session))
        {
            if (session.TrySetStatus(ModelDownloadStatus.Downloading, ModelDownloadStatus.Paused))
            {
                session.CancellationTokenSource.Cancel();

                var downloader = GetDownloader(sourceId);
                var result = await downloader.PauseDownloadAsync(sourceId, cancellationToken);

                if (!result)
                {
                    // Revert status if pause failed
                    session.TrySetStatus(ModelDownloadStatus.Paused, ModelDownloadStatus.Downloading);
                }

                return result;
            }

            _logger.LogWarning("Cannot pause download for {SourceId} - current status: {Status}",
                sourceId, session.Status);
            return false;
        }

        // No active session, try downloader directly
        var fallbackDownloader = GetDownloader(sourceId);
        return await fallbackDownloader.PauseDownloadAsync(sourceId, cancellationToken);
    }

    /// <summary>
    /// Resumes a paused download with state validation
    /// </summary>
    public async Task<LMModel> ResumeDownloadAsync(
        string sourceId,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var downloader = GetDownloader(sourceId);

        // Verify download can be resumed
        var currentStatus = await downloader.GetDownloadStatusAsync(sourceId, cancellationToken);
        if (currentStatus != ModelDownloadStatus.Paused)
        {
            throw new InvalidOperationException(
                $"Cannot resume download for {sourceId} - current status: {currentStatus}");
        }

        // Remove old session if exists
        if (_activeSessions.TryRemove(sourceId, out var oldSession))
        {
            oldSession.CancellationTokenSource.Dispose();
        }

        // Create new session for resume
        var session = new DownloadSession
        {
            ModelId = sourceId,
        };

        // Set status to Downloading immediately
        session.ForceSetStatus(ModelDownloadStatus.Downloading);
        _activeSessions[sourceId] = session;

        await _downloadSemaphore.WaitAsync(cancellationToken);
        try
        {
            var progressWrapper = new Progress<ModelDownloadProgress>(p =>
            {
                // Ensure progress status is Downloading
                var adjustedProgress = new ModelDownloadProgress
                {
                    ModelId = p.ModelId,
                    FileName = p.FileName,
                    BytesDownloaded = p.BytesDownloaded,
                    TotalBytes = p.TotalBytes,
                    BytesPerSecond = p.BytesPerSecond,
                    EstimatedTimeRemaining = p.EstimatedTimeRemaining,
                    Status = p.Status == ModelDownloadStatus.Paused ? ModelDownloadStatus.Downloading : p.Status,
                    ErrorMessage = p.ErrorMessage
                };

                session.UpdateProgress(adjustedProgress);
                progress?.Report(adjustedProgress);
            });

            // Create resume task
            var resumeTask = downloader.ResumeDownloadAsync(sourceId, progressWrapper, session.CancellationTokenSource.Token);
            session.DownloadTask = resumeTask;

            var result = await resumeTask;

            session.Status = ModelDownloadStatus.Completed;
            return result;
        }
        catch (OperationCanceledException)
        {
            session.Status = ModelDownloadStatus.Cancelled;
            throw;
        }
        catch (Exception)
        {
            session.Status = ModelDownloadStatus.Failed;
            throw;
        }
        finally
        {
            _downloadSemaphore.Release();
        }
    }

    /// <summary>
    /// Cancels a download with proper cleanup
    /// </summary>
    public async Task<bool> CancelDownloadAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        bool sessionCancelled = false;

        if (_activeSessions.TryGetValue(sourceId, out var session))
        {
            session.Status = ModelDownloadStatus.Cancelled;
            session.CancellationTokenSource.Cancel();
            sessionCancelled = true;
        }

        var downloader = GetDownloader(sourceId);
        var downloaderResult = await downloader.CancelDownloadAsync(sourceId, cancellationToken);

        // Clean up session
        if (_activeSessions.TryRemove(sourceId, out var removedSession))
        {
            removedSession.CancellationTokenSource.Dispose();
        }

        return sessionCancelled || downloaderResult;
    }

    /// <summary>
    /// Gets unified download status with proper priority handling
    /// </summary>
    public ModelDownloadStatus? GetDownloadStatus(string sourceId)
    {
        // Check active session first - this takes priority
        if (_activeSessions.TryGetValue(sourceId, out var session))
        {
            var sessionStatus = session.Status;

            // If session is active and downloading, return that status
            if (sessionStatus == ModelDownloadStatus.Downloading || sessionStatus == ModelDownloadStatus.Initializing)
            {
                return sessionStatus;
            }
        }

        // Fallback to downloader status check for non-active states
        try
        {
            var downloader = GetDownloader(sourceId);
            var task = downloader.GetDownloadStatusAsync(sourceId, CancellationToken.None);

            if (task.Wait(TimeSpan.FromSeconds(2)) && task.IsCompletedSuccessfully)
            {
                var downloaderStatus = task.Result;

                // If we have a session but downloader says completed, sync the session
                if (session != null && downloaderStatus == ModelDownloadStatus.Completed)
                {
                    session.ForceSetStatus(ModelDownloadStatus.Completed);
                }

                return downloaderStatus;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get download status for {SourceId}", sourceId);
        }

        // Return session status as fallback
        return session?.Status;
    }

    /// <summary>
    /// Gets download progress from active session
    /// </summary>
    public ModelDownloadProgress? GetDownloadProgress(string sourceId)
    {
        if (_activeSessions.TryGetValue(sourceId, out var session))
        {
            return session.LastProgress;
        }

        return null;
    }

    /// <summary>
    /// Gets all current download information
    /// </summary>
    public async Task<IEnumerable<DownloadInfo>> GetAllDownloadsAsync(CancellationToken cancellationToken = default)
    {
        var downloads = new List<DownloadInfo>();

        // Add active sessions
        foreach (var kvp in _activeSessions)
        {
            var session = kvp.Value;
            downloads.Add(new DownloadInfo
            {
                ModelId = session.ModelId,
                Status = session.Status,
                StartedAt = session.StartTime,
                Progress = session.LastProgress
            });
        }

        // Add paused downloads from file system
        var modelsPath = Path.Combine(_options.DataPath, "models");
        if (Directory.Exists(modelsPath))
        {
            foreach (var collectionDir in Directory.GetDirectories(modelsPath))
            {
                var downloadFiles = Directory.GetFiles(collectionDir, "*.download");
                foreach (var downloadFile in downloadFiles)
                {
                    try
                    {
                        var state = DownloadStateHelper.LoadFromFile(downloadFile);

                        // Skip if already in active sessions
                        if (_activeSessions.ContainsKey(state.ModelId))
                            continue;

                        downloads.Add(new DownloadInfo
                        {
                            ModelId = state.ModelId,
                            Status = ModelDownloadStatus.Paused,
                            StartedAt = state.StartedAt,
                            Progress = CreateProgressFromState(state)
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to load download state from {File}", downloadFile);
                    }
                }
            }
        }

        return downloads.OrderByDescending(d => d.StartedAt);
    }

    /// <summary>
    /// Validates and synchronizes download statuses periodically
    /// </summary>
    private void ValidateDownloadStatuses(object? state)
    {
        var completedSessions = new List<string>();

        foreach (var kvp in _activeSessions.ToList())
        {
            var sourceId = kvp.Key;
            var session = kvp.Value;

            if (session.DownloadTask?.IsCompleted == true)
            {
                if (session.DownloadTask.IsFaulted)
                {
                    session.Status = ModelDownloadStatus.Failed;
                    _logger.LogError(session.DownloadTask.Exception,
                        "Download failed for {SourceId}", sourceId);
                }
                else if (session.DownloadTask.IsCanceled)
                {
                    session.Status = ModelDownloadStatus.Cancelled;
                }
                else
                {
                    session.Status = ModelDownloadStatus.Completed;
                }

                completedSessions.Add(sourceId);
            }
        }

        // Clean up completed sessions after delay
        foreach (var sourceId in completedSessions)
        {
            _ = Task.Delay(TimeSpan.FromMinutes(1)).ContinueWith(_ =>
            {
                if (_activeSessions.TryRemove(sourceId, out var removedSession))
                {
                    removedSession.CancellationTokenSource.Dispose();
                }
            });
        }
    }

    private ModelDownloadProgress CreateProgressFromState(DownloadState state)
    {
        var targetFile = Path.Combine(state.TargetDirectory, state.DownloadingFileName);
        var downloadedBytes = File.Exists(targetFile) ? new FileInfo(targetFile).Length : state.DownloadedBytes;

        return new ModelDownloadProgress
        {
            ModelId = state.ModelId,
            FileName = state.DownloadingFileName,
            BytesDownloaded = downloadedBytes,
            TotalBytes = state.TotalSize,
            BytesPerSecond = 0,
            Status = ModelDownloadStatus.Paused
        };
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
            _statusUpdateTimer?.Dispose();

            foreach (var session in _activeSessions.Values)
            {
                session.CancellationTokenSource.Cancel();
                session.CancellationTokenSource.Dispose();
            }
            _activeSessions.Clear();

            _downloadSemaphore.Dispose();
            _disposed = true;
        }

        GC.SuppressFinalize(this);
    }
}