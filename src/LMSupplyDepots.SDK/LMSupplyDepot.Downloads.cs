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
    /// Gets detailed download progress including status, size, and timing information
    /// </summary>
    public async Task<ModelDownloadProgress?> GetDownloadProgressAsync(
        string modelKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            string modelId = await ModelManager.ResolveModelKeyAsync(modelKey, cancellationToken);

            var status = ModelManager.GetDownloadStatus(modelId);
            if (status == null) return null;

            var progress = ModelManager.GetDownloadProgress(modelId);
            var allDownloads = await ModelManager.GetAllDownloadsAsync(cancellationToken);
            var repository = _serviceProvider.GetRequiredService<IModelRepository>();

            return await DownloadProgressCalculator.CreateDetailedProgressAsync(
                modelId, status, progress, repository, allDownloads, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get download progress for {ModelKey}", modelKey);
            return DownloadProgressCalculator.CreateNotFoundProgress(modelKey);
        }
    }

    /// <summary>
    /// Gets information about all current downloads
    /// </summary>
    public async Task<IEnumerable<DownloadInfo>> GetAllDownloadsAsync(CancellationToken cancellationToken = default)
    {
        return await ModelManager.GetAllDownloadsAsync(cancellationToken);
    }
}