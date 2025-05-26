namespace LMSupplyDepots.ModelHub.HuggingFace;

/// <summary>
/// Implementation of download management operations
/// </summary>
public partial class HuggingFaceDownloader
{
    /// <summary>
    /// Pauses a download that is in progress
    /// </summary>
    public async Task<bool> PauseDownloadAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Attempting to pause download for model {ModelId}", sourceId);

        // Check current status first
        var currentStatus = await GetDownloadStatusAsync(sourceId, cancellationToken);
        if (currentStatus != ModelDownloadStatus.Downloading)
        {
            _logger.LogWarning("Cannot pause download for model {ModelId} - not in downloading state", sourceId);
            return false;
        }

        // Simply return true - actual pause is handled by cancellation in DownloadManager
        _logger.LogInformation("Download marked for pause for model {ModelId}", sourceId);
        return true;
    }

    /// <summary>
    /// Cancels a download that is in progress or paused
    /// </summary>
    public async Task<bool> CancelDownloadAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Attempting to cancel download for model {ModelId}", sourceId);

        try
        {
            // Remove status file to cancel download
            DownloadStatusHelper.RemoveStatusFile(sourceId, _hubOptions.DataPath);

            // Clean up any partial downloads
            await CleanupPartialDownloadAsync(sourceId, cancellationToken);

            _logger.LogInformation("Download cancelled for model {ModelId}", sourceId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling download for model {ModelId}", sourceId);
            return false;
        }
    }

    /// <summary>
    /// Gets the current status of a download
    /// </summary>
    public async Task<ModelDownloadStatus?> GetDownloadStatusAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if there's an existing model
            var existingModel = await _fileSystemRepository.GetModelAsync(sourceId, cancellationToken);
            if (existingModel != null && existingModel.IsLocal)
            {
                return ModelDownloadStatus.Completed;
            }

            // Check if download is paused (status file exists)
            if (DownloadStatusHelper.IsPaused(sourceId, _hubOptions.DataPath))
            {
                return ModelDownloadStatus.Paused;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting download status for model {ModelId}", sourceId);
            return null;
        }
    }

    /// <summary>
    /// Resumes a previously paused download
    /// </summary>
    public async Task<LMModel> ResumeDownloadAsync(
        string sourceId,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Attempting to resume download for model {ModelId}", sourceId);

        var downloadStatus = await GetDownloadStatusAsync(sourceId, cancellationToken);
        if (downloadStatus != ModelDownloadStatus.Paused)
        {
            _logger.LogWarning("Cannot resume download for model {ModelId} - not in paused state", sourceId);
            throw new InvalidOperationException($"Download for model {sourceId} is not paused");
        }

        try
        {
            var normalizedId = HuggingFaceHelper.NormalizeSourceId(sourceId);
            var modelType = await HuggingFaceHelper.DetermineModelTypeAsync(normalizedId, _client.Value, cancellationToken);
            var targetDirectory = _fileSystemRepository.GetModelDirectoryPath(sourceId, modelType);

            Directory.CreateDirectory(targetDirectory);

            _logger.LogInformation("Resuming download for model {ModelId} to {TargetDir}", sourceId, targetDirectory);
            return await DownloadModelAsync(sourceId, targetDirectory, progress, cancellationToken);
        }
        catch (Exception ex) when (!(ex is InvalidOperationException))
        {
            _logger.LogError(ex, "Error resuming download for model {ModelId}", sourceId);
            throw new InvalidOperationException($"Failed to resume download for model {sourceId}", ex);
        }
    }

    /// <summary>
    /// Cleans up partial download files
    /// </summary>
    private async Task CleanupPartialDownloadAsync(string sourceId, CancellationToken cancellationToken)
    {
        try
        {
            if (!ModelIdentifier.TryParse(sourceId, out var identifier))
            {
                return;
            }

            var normalizedId = HuggingFaceHelper.NormalizeSourceId(sourceId);
            var modelType = await HuggingFaceHelper.DetermineModelTypeAsync(normalizedId, _client.Value, cancellationToken);

            var targetDirectory = _fileSystemRepository.GetModelDirectoryPath(sourceId, modelType);
            if (Directory.Exists(targetDirectory))
            {
                var files = Directory.GetFiles(targetDirectory);

                // Only delete if directory contains only temporary/partial files
                if (files.Length == 0 || files.All(f =>
                    Path.GetExtension(f).ToLowerInvariant() is ".download" or ".part" or ".tmp"))
                {
                    try
                    {
                        Directory.Delete(targetDirectory, true);
                        _logger.LogInformation("Removed partial download directory for model {ModelId}", sourceId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not delete directory for model {ModelId}", sourceId);
                    }
                }
                else
                {
                    _logger.LogInformation(
                        "Directory for model {ModelId} contains non-temporary files and will not be deleted",
                        sourceId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during cleanup for model {ModelId}", sourceId);
        }
    }
}