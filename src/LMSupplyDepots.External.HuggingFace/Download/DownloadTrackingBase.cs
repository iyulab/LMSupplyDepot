using System.Collections.Concurrent;

namespace LMSupplyDepots.External.HuggingFace.Download;

/// <summary>
/// Provides base functionality for tracking model downloads.
/// Implementations should provide specific storage mechanisms.
/// </summary>
public abstract class DownloadTrackingBase : IDisposable
{
    private bool _disposed;
    private readonly ConcurrentDictionary<string, ModelFileDownloadState> _activeDownloads = new();

    /// <summary>
    /// Occurs when a download starts
    /// </summary>
    public event EventHandler<ModelFileDownloadState>? DownloadStarted;

    /// <summary>
    /// Occurs when download progress is updated
    /// </summary>
    public event EventHandler<ModelFileDownloadState>? DownloadProgressUpdated;

    /// <summary>
    /// Occurs when a download completes
    /// </summary>
    public event EventHandler<ModelFileDownloadState>? DownloadCompleted;

    /// <summary>
    /// Records the start of a new download
    /// </summary>
    public virtual async Task RecordDownloadStartAsync(
        string modelId,
        string filePath,
        long totalSize)
    {
        ThrowIfDisposed();

        var state = new ModelFileDownloadState
        {
            ModelId = modelId,
            FilePath = filePath,
            TotalSize = totalSize,
            LastAttempt = DateTime.UtcNow
        };

        _activeDownloads[GetDownloadKey(modelId, filePath)] = state;
        await SaveDownloadStateAsync(state);

        DownloadStarted?.Invoke(this, state);
    }

    /// <summary>
    /// Updates the progress of an ongoing download
    /// </summary>
    public virtual async Task UpdateProgressAsync(
        string modelId,
        string filePath,
        long downloadedSize,
        bool isCompleted = false)
    {
        ThrowIfDisposed();

        var key = GetDownloadKey(modelId, filePath);
        if (_activeDownloads.TryGetValue(key, out var state))
        {
            state.DownloadedSize = downloadedSize;
            state.IsCompleted = isCompleted;
            state.LastAttempt = DateTime.UtcNow;

            await SaveDownloadStateAsync(state);

            if (isCompleted)
            {
                _activeDownloads.TryRemove(key, out _);
                DownloadCompleted?.Invoke(this, state);
            }
            else
            {
                DownloadProgressUpdated?.Invoke(this, state);
            }
        }
    }

    /// <summary>
    /// Gets the last known position for resuming a download
    /// </summary>
    public virtual async Task<long> GetResumePositionAsync(string modelId, string filePath)
    {
        ThrowIfDisposed();
        var state = await LoadDownloadStateAsync(modelId, filePath);
        return state?.DownloadedSize ?? 0;
    }

    /// <summary>
    /// Gets all incomplete downloads
    /// </summary>
    public virtual async Task<IReadOnlyList<ModelFileDownloadState>> GetIncompleteDownloadsAsync()
    {
        ThrowIfDisposed();
        return await LoadIncompleteDownloadsAsync();
    }

    /// <summary>
    /// Removes tracking information for completed downloads
    /// </summary>
    public virtual async Task CleanupCompletedDownloadsAsync()
    {
        ThrowIfDisposed();
        await RemoveCompletedDownloadsAsync();
    }

    protected virtual string GetDownloadKey(string modelId, string filePath)
    {
        return $"{modelId}:{filePath}";
    }

    #region Abstract Methods for Implementation

    /// <summary>
    /// Saves the download state to persistent storage
    /// </summary>
    protected abstract Task SaveDownloadStateAsync(ModelFileDownloadState state);

    /// <summary>
    /// Loads a specific download state from persistent storage
    /// </summary>
    protected abstract Task<ModelFileDownloadState?> LoadDownloadStateAsync(string modelId, string filePath);

    /// <summary>
    /// Loads all incomplete downloads from persistent storage
    /// </summary>
    protected abstract Task<List<ModelFileDownloadState>> LoadIncompleteDownloadsAsync();

    /// <summary>
    /// Removes completed downloads from persistent storage
    /// </summary>
    protected abstract Task RemoveCompletedDownloadsAsync();

    #endregion

    #region IDisposable Implementation

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _activeDownloads.Clear();
            }
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, GetType().Name);
    }

    #endregion
}