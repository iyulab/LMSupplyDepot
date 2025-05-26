namespace LMSupplyDepots.SDK;

/// <summary>
/// Download management functionality for LMSupplyDepot
/// </summary>
public partial class LMSupplyDepot
{

    /// <summary>
    /// Downloads a specific model artifact from an external source
    /// </summary>
    public Task<LMModel> DownloadModelAsync(
        string modelKey,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return ModelManager.DownloadModelAsync(modelKey, progress, cancellationToken);
    }

    /// <summary>
    /// Pauses an active model download
    /// </summary>
    public async Task<bool> PauseDownloadAsync(
        string modelKey,
        CancellationToken cancellationToken = default)
    {
        string modelId = await ModelManager.ResolveModelKeyAsync(modelKey, cancellationToken);
        return await ModelManager.PauseDownloadAsync(modelId, cancellationToken);
    }

    /// <summary>
    /// Resumes a paused model download
    /// </summary>
    public async Task<LMModel> ResumeDownloadAsync(
        string modelKey,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string modelId = await ModelManager.ResolveModelKeyAsync(modelKey, cancellationToken);
        return await ModelManager.ResumeDownloadAsync(modelId, progress, cancellationToken);
    }

    /// <summary>
    /// Cancels an active or paused model download
    /// </summary>
    public async Task<bool> CancelDownloadAsync(
        string modelKey,
        CancellationToken cancellationToken = default)
    {
        string modelId = await ModelManager.ResolveModelKeyAsync(modelKey, cancellationToken);
        return await ModelManager.CancelDownloadAsync(modelId, cancellationToken);
    }

    /// <summary>
    /// Gets the current status of a model download
    /// </summary>
    public async Task<ModelDownloadStatus?> GetDownloadStatusAsync(string modelKey, CancellationToken cancellationToken = default)
    {
        string modelId = await ModelManager.ResolveModelKeyAsync(modelKey, cancellationToken);
        return ModelManager.GetDownloadStatus(modelId);
    }

    /// <summary>
    /// Gets download progress information
    /// </summary>
    public async Task<ModelDownloadProgress?> GetDownloadProgressAsync(string modelKey, CancellationToken cancellationToken = default)
    {
        string modelId = await ModelManager.ResolveModelKeyAsync(modelKey, cancellationToken);
        return ModelManager.GetDownloadProgress(modelId);
    }

    /// <summary>
    /// Gets information about all current downloads
    /// </summary>
    public async Task<IEnumerable<DownloadInfo>> GetAllDownloadsAsync(CancellationToken cancellationToken = default)
    {
        return await ModelManager.GetAllDownloadsAsync(cancellationToken);
    }

}