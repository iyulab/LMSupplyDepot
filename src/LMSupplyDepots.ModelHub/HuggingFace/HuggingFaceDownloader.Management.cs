using LMSupplyDepots.External.HuggingFace.Download;

namespace LMSupplyDepots.ModelHub.HuggingFace;

/// <summary>
/// Implementation of download management operations with proper cancellation
/// </summary>
public partial class HuggingFaceDownloader
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _downloadCancellationTokens = new();

    /// <summary>
    /// Pauses a download by cancelling the operation and saving current state
    /// </summary>
    public async Task<bool> PauseDownloadAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Attempting to pause download for model {ModelId}", sourceId);

        // Cancel the active download immediately
        if (_downloadCancellationTokens.TryRemove(sourceId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
            _logger.LogInformation("Cancelled active download for {ModelId}", sourceId);
        }

        // Check current status
        var currentStatus = await GetDownloadStatusAsync(sourceId, cancellationToken);

        if (currentStatus == null)
        {
            _logger.LogWarning("No download found for model {ModelId}", sourceId);
            return false;
        }

        if (currentStatus == ModelDownloadStatus.Completed)
        {
            _logger.LogInformation("Download for model {ModelId} is already completed", sourceId);
            return false;
        }

        // Ensure the download state reflects paused status
        if (ModelIdentifier.TryParse(sourceId, out var identifier))
        {
            var modelDir = GetModelDirectoryPath(identifier);
            if (Directory.Exists(modelDir))
            {
                // Validate that files are partially downloaded
                var downloadFiles = Directory.GetFiles(modelDir, "*.download");
                if (downloadFiles.Length > 0)
                {
                    _logger.LogInformation("Download state files found, marking as paused for {ModelId}", sourceId);
                    return true;
                }
            }
        }

        _logger.LogInformation("Download paused for model {ModelId}", sourceId);
        return true;
    }

    /// <summary>
    /// Cancels a download completely and cleans up partial files
    /// </summary>
    public async Task<bool> CancelDownloadAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Attempting to cancel download for model {ModelId}", sourceId);

        // Cancel the active download immediately
        if (_downloadCancellationTokens.TryRemove(sourceId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
            _logger.LogInformation("Cancelled active download for {ModelId}", sourceId);
        }

        try
        {
            await CleanupPartialDownloadAsync(sourceId, cancellationToken);
            _logger.LogInformation("Download cancelled and cleaned up for model {ModelId}", sourceId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling download for model {ModelId}", sourceId);
            return false;
        }
    }

    /// <summary>
    /// Gets the current status of a download with improved accuracy
    /// </summary>
    public async Task<ModelDownloadStatus?> GetDownloadStatusAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if there's an active download first
            if (_downloadCancellationTokens.ContainsKey(sourceId))
            {
                return ModelDownloadStatus.Downloading;
            }

            // Check if there's a completed model
            var existingModel = await _fileSystemRepository.GetModelAsync(sourceId, cancellationToken);
            if (existingModel != null && existingModel.IsLocal)
            {
                return ModelDownloadStatus.Completed;
            }

            // Check download state files
            if (ModelIdentifier.TryParse(sourceId, out var identifier))
            {
                var modelDir = GetModelDirectoryPath(identifier);
                if (Directory.Exists(modelDir))
                {
                    var downloadFiles = Directory.GetFiles(modelDir, "*.download");
                    if (downloadFiles.Length > 0)
                    {
                        // Verify if download is actually complete
                        if (DownloadStateHelper.IsDownloadComplete(modelDir, sourceId))
                        {
                            // Clean up download state files for completed downloads
                            DownloadStateHelper.CleanupCompletedDownloads(sourceId, modelDir);
                            return ModelDownloadStatus.Completed;
                        }

                        return ModelDownloadStatus.Paused;
                    }

                    // Check if model files exist without download state
                    if (FileSystemHelper.ContainsModelFiles(modelDir))
                    {
                        var jsonFiles = Directory.GetFiles(modelDir, "*.json");
                        if (jsonFiles.Length > 0)
                        {
                            return ModelDownloadStatus.Completed;
                        }
                    }
                }
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
    /// Resumes a previously paused download using the startFrom parameter
    /// </summary>
    public async Task<LMModel> ResumeDownloadAsync(
        string sourceId,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Attempting to resume download for model {ModelId}", sourceId);

        var downloadStatus = await GetDownloadStatusAsync(sourceId, cancellationToken);

        if (downloadStatus == ModelDownloadStatus.Completed)
        {
            _logger.LogInformation("Download for model {ModelId} is already completed", sourceId);
            var existingModel = await _fileSystemRepository.GetModelAsync(sourceId, cancellationToken);
            if (existingModel != null)
            {
                return existingModel;
            }
        }

        if (downloadStatus != ModelDownloadStatus.Paused)
        {
            _logger.LogWarning("Cannot resume download for model {ModelId} - current state: {Status}", sourceId, downloadStatus);
            throw new InvalidOperationException($"Download for model {sourceId} is not paused (current status: {downloadStatus})");
        }

        try
        {
            var (repoId, artifactName) = HuggingFaceHelper.NormalizeAndSplitSourceId(sourceId);
            var identifier = ModelIdentifier.Parse(sourceId);
            var targetDirectory = GetModelDirectoryPath(identifier);

            Directory.CreateDirectory(targetDirectory);

            _logger.LogInformation("Resuming download for model {ModelId} to {TargetDir}", sourceId, targetDirectory);

            // Create a combined cancellation token source for this download
            using var downloadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            RegisterDownloadCancellationToken(sourceId, downloadCts);

            try
            {
                // Get model info
                var hfModel = await GetHuggingFaceModelAsync(repoId, sourceId, downloadCts.Token);
                var model = HuggingFaceHelper.ConvertToLMModel(hfModel);

                // Determine what file(s) to resume
                List<string> filesToDownload;
                if (!string.IsNullOrEmpty(artifactName))
                {
                    model.ArtifactName = artifactName;
                    model.Id = $"{model.Registry}:{model.RepoId}/{artifactName}";
                    filesToDownload = HuggingFaceHelper.FindArtifactFiles(hfModel, artifactName);

                    if (filesToDownload.Count == 0)
                    {
                        var exactFilename = $"{artifactName}.gguf";
                        filesToDownload.Add(exactFilename);
                    }
                }
                else
                {
                    filesToDownload = HuggingFaceHelper.GetAvailableModelFiles(hfModel);
                    if (filesToDownload.Count == 0)
                    {
                        throw new ModelNotFoundException(sourceId, "No model files found in repository");
                    }
                }

                model.LocalPath = targetDirectory;

                // Create progress reporter that reports Downloading status
                var progressReporter = CreateResumeProgressReporter(sourceId, targetDirectory, progress);

                // Resume each file individually
                foreach (var file in filesToDownload)
                {
                    await ResumeFileDownloadAsync(repoId, file, targetDirectory, sourceId, progressReporter, downloadCts.Token);
                }

                // Update model with actual downloaded file info
                await UpdateModelWithActualFilesAsync(model, targetDirectory, downloadCts.Token);

                // Create metadata
                await CreateModelMetadataAsync(model, targetDirectory, downloadCts.Token);

                // Remove download state files on success
                foreach (var file in filesToDownload)
                {
                    DownloadStateHelper.RemoveDownloadStateFile(targetDirectory, file);
                }

                DownloadStateHelper.CleanupCompletedDownloads(sourceId, targetDirectory);

                _logger.LogInformation("Resume completed for model {ModelId}", sourceId);
                return model;
            }
            finally
            {
                UnregisterDownloadCancellationToken(sourceId);
            }
        }
        catch (Exception ex) when (!(ex is InvalidOperationException))
        {
            _logger.LogError(ex, "Error resuming download for model {ModelId}", sourceId);
            throw new InvalidOperationException($"Failed to resume download for model {sourceId}", ex);
        }
    }

    /// <summary>
    /// Creates a progress reporter that ensures Downloading status for resume operations
    /// </summary>
    private IProgress<ModelDownloadProgress> CreateResumeProgressReporter(
        string sourceId,
        string targetDirectory,
        IProgress<ModelDownloadProgress>? progress)
    {
        if (progress == null)
            return new Progress<ModelDownloadProgress>();

        return new Progress<ModelDownloadProgress>(p =>
        {
            // Force status to Downloading for resume operations
            var adjustedProgress = new ModelDownloadProgress
            {
                ModelId = p.ModelId,
                FileName = p.FileName,
                BytesDownloaded = p.BytesDownloaded,
                TotalBytes = p.TotalBytes,
                BytesPerSecond = p.BytesPerSecond,
                EstimatedTimeRemaining = p.EstimatedTimeRemaining,
                Status = p.Status == ModelDownloadStatus.Completed ? ModelDownloadStatus.Completed : ModelDownloadStatus.Downloading,
                ErrorMessage = p.ErrorMessage
            };

            progress.Report(adjustedProgress);
        });
    }

    /// <summary>
    /// Resumes download for a specific file using startFrom parameter
    /// </summary>
    private async Task ResumeFileDownloadAsync(
        string repoId,
        string fileName,
        string targetDirectory,
        string sourceId,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var outputPath = Path.Combine(targetDirectory, fileName);

        // Get current downloaded size
        long startFrom = 0;
        if (File.Exists(outputPath))
        {
            var fileInfo = new FileInfo(outputPath);
            startFrom = fileInfo.Length;
            _logger.LogInformation("Resuming {FileName} from byte position {StartFrom}", fileName, startFrom);
        }

        // Get expected total size from download state
        var downloadStates = DownloadStateHelper.GetAllDownloadStates(sourceId, targetDirectory);
        var fileState = downloadStates.FirstOrDefault(s => s.DownloadingFileName == fileName);

        if (fileState == null)
        {
            _logger.LogWarning("No download state found for {FileName}, cannot resume", fileName);
            return;
        }

        // Check if file is already complete
        if (startFrom >= fileState.TotalSize && fileState.TotalSize > 0)
        {
            _logger.LogInformation("File {FileName} is already complete", fileName);
            return;
        }

        // Create progress wrapper that reports absolute file size, not incremental
        var progressWrapper = progress != null ? new Progress<FileDownloadProgress>(p =>
        {
            // External client reports CurrentBytes as the amount downloaded in this session
            // We need to calculate the total absolute position
            var absoluteBytesDownloaded = startFrom + p.CurrentBytes;

            // Ensure we don't exceed the expected total size
            var cappedBytesDownloaded = fileState.TotalSize > 0
                ? Math.Min(absoluteBytesDownloaded, fileState.TotalSize)
                : absoluteBytesDownloaded;

            var modelProgress = new ModelDownloadProgress
            {
                ModelId = sourceId,
                FileName = fileName,
                BytesDownloaded = cappedBytesDownloaded,
                TotalBytes = fileState.TotalSize,
                BytesPerSecond = p.DownloadSpeed,
                EstimatedTimeRemaining = p.RemainingTime,
                Status = p.IsCompleted ? ModelDownloadStatus.Completed : ModelDownloadStatus.Downloading
            };
            progress.Report(modelProgress);
        }) : null;

        try
        {
            // Resume download using HuggingFace client's startFrom parameter
            var result = await _client.Value.DownloadFileWithResultAsync(
                repoId,
                fileName,
                outputPath,
                startFrom, // Resume from current position
                progressWrapper,
                cancellationToken);

            if (result.IsCompleted)
            {
                _logger.LogInformation("File {FileName} download resumed and completed", fileName);

                // Get final file size and update download state
                var finalFileInfo = new FileInfo(outputPath);
                var finalSize = finalFileInfo.Length;

                // Cap the final size at expected total size
                var effectiveFinalSize = fileState.TotalSize > 0
                    ? Math.Min(finalSize, fileState.TotalSize)
                    : finalSize;

                await DownloadStateHelper.UpdateDownloadProgressAsync(
                    targetDirectory,
                    fileName,
                    effectiveFinalSize,
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resume download for file {FileName}", fileName);
            throw;
        }
    }

    /// <summary>
    /// Registers a cancellation token for an active download
    /// </summary>
    internal void RegisterDownloadCancellationToken(string sourceId, CancellationTokenSource cts)
    {
        _downloadCancellationTokens[sourceId] = cts;
    }

    /// <summary>
    /// Unregisters a cancellation token when download completes
    /// </summary>
    internal void UnregisterDownloadCancellationToken(string sourceId)
    {
        if (_downloadCancellationTokens.TryRemove(sourceId, out var cts))
        {
            cts.Dispose();
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

            var targetDirectory = GetModelDirectoryPath(identifier);
            if (Directory.Exists(targetDirectory))
            {
                // Remove all download state files for this model
                DownloadStateHelper.RemoveAllDownloadStateFiles(sourceId, targetDirectory);

                var files = Directory.GetFiles(targetDirectory);

                // Only delete if directory contains only temporary/partial files or is empty
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

    /// <summary>
    /// Gets the model directory path for a model identifier
    /// </summary>
    private string GetModelDirectoryPath(ModelIdentifier identifier)
    {
        return FileSystemHelper.GetModelDirectoryPath(identifier, _hubOptions.DataPath);
    }

    /// <summary>
    /// Clean up cancellation tokens when disposing
    /// </summary>
    private void CleanupCancellationTokens()
    {
        // Cancel and dispose all active downloads
        foreach (var cts in _downloadCancellationTokens.Values)
        {
            try
            {
                cts.Cancel();
                cts.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error disposing cancellation token");
            }
        }
        _downloadCancellationTokens.Clear();
    }
}