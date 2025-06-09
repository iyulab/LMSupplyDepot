namespace LMSupplyDepots.ModelHub.HuggingFace;

/// <summary>
/// Download job management operations (pause, resume, cancel, status)
/// </summary>
public partial class HuggingFaceDownloader
{
    /// <summary>
    /// Pauses a download by checking current state (cancellation handled externally)
    /// </summary>
    public async Task<bool> PauseDownloadAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        var currentStatus = await GetDownloadStatusAsync(sourceId, cancellationToken);

        // If already completed, return false
        if (currentStatus == ModelDownloadStatus.Completed)
        {
            return false;
        }

        // If already paused or has state files, return true
        if (currentStatus == ModelDownloadStatus.Paused)
        {
            return true;
        }

        // External cancellation should have stopped any active download
        return currentStatus != ModelDownloadStatus.Downloading;
    }

    /// <summary>
    /// Resumes a previously paused download
    /// </summary>
    public async Task<LMModel> ResumeDownloadAsync(
        string sourceId,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Resuming download for model {ModelId}", sourceId);

        var downloadStatus = await GetDownloadStatusAsync(sourceId, cancellationToken);

        // Validate current state
        if (downloadStatus == ModelDownloadStatus.Completed)
        {
            var existingModel = await _fileSystemRepository.GetModelAsync(sourceId, cancellationToken);
            if (existingModel != null)
            {
                _logger.LogInformation("Model {ModelId} is already completed", sourceId);
                return existingModel;
            }
        }

        if (downloadStatus != ModelDownloadStatus.Paused)
        {
            throw new InvalidOperationException($"Cannot resume model {sourceId} - current status: {downloadStatus}");
        }

        try
        {
            var (repoId, artifactName) = HuggingFaceHelper.NormalizeAndSplitSourceId(sourceId);
            var identifier = ModelIdentifier.Parse(sourceId);
            var targetDirectory = GetModelDirectoryPath(identifier);

            Directory.CreateDirectory(targetDirectory);

            // Get model info
            var hfModel = await GetHuggingFaceModelAsync(repoId, sourceId, cancellationToken);
            var model = HuggingFaceHelper.ConvertToLMModel(hfModel);

            // Determine files to resume
            var filesToDownload = GetFilesToDownload(hfModel, artifactName, sourceId);
            model.LocalPath = targetDirectory;

            // Create progress reporter
            var progressReporter = CreateProgressReporter(sourceId, targetDirectory, null, progress, cancellationToken);

            // Resume each file
            foreach (var file in filesToDownload)
            {
                await ResumeFileDownloadAsync(repoId, file, targetDirectory, cancellationToken);
            }

            // Finalize model
            await UpdateModelWithActualFilesAsync(model, targetDirectory, cancellationToken);
            await CreateModelMetadataAsync(model, targetDirectory, cancellationToken);

            // Cleanup download state files
            foreach (var file in filesToDownload)
            {
                DownloadStateHelper.RemoveDownloadStateFile(targetDirectory, file);
            }

            DownloadStateHelper.CleanupCompletedDownloads(sourceId, targetDirectory);

            _logger.LogInformation("Resume completed for model {ModelId}", sourceId);
            return model;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Resume operation was cancelled for model {ModelId}", sourceId);
            throw;
        }
        catch (Exception ex) when (!(ex is InvalidOperationException))
        {
            _logger.LogError(ex, "Error resuming download for model {ModelId}", sourceId);
            throw new InvalidOperationException($"Failed to resume download for model {sourceId}", ex);
        }
    }

    /// <summary>
    /// Cancels a download with cleanup
    /// </summary>
    public async Task<bool> CancelDownloadAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        try
        {
            await CleanupPartialDownloadAsync(sourceId, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cleanup for model {ModelId}", sourceId);
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
            // Check if model is completed in repository
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
    /// Resumes download for a specific file
    /// </summary>
    private async Task ResumeFileDownloadAsync(
        string repoId,
        string fileName,
        string targetDirectory,
        CancellationToken cancellationToken)
    {
        var outputPath = Path.Combine(targetDirectory, fileName);

        // Get current downloaded size for resume
        long startFrom = 0;
        if (File.Exists(outputPath))
        {
            var fileInfo = new FileInfo(outputPath);
            startFrom = fileInfo.Length;
            _logger.LogInformation("Resuming {FileName} from byte position {StartFrom}", fileName, startFrom);
        }

        try
        {
            // Create progress wrapper with cancellation checks
            var progressWrapper = new Progress<External.HuggingFace.Download.FileDownloadProgress>(p =>
            {
                cancellationToken.ThrowIfCancellationRequested();
            });

            // Resume download from current position
            var result = await _client.Value.DownloadFileWithResultAsync(
                repoId,
                fileName,
                outputPath,
                startFrom,
                progressWrapper,
                cancellationToken);

            if (result.IsCompleted)
            {
                _logger.LogInformation("File {FileName} download completed", fileName);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("File {FileName} download was cancelled", fileName);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resume download for file {FileName}", fileName);
            throw;
        }
    }

    /// <summary>
    /// Cleans up partial download files
    /// </summary>
    private async Task CleanupPartialDownloadAsync(string sourceId, CancellationToken cancellationToken)
    {
        if (!ModelIdentifier.TryParse(sourceId, out var identifier))
        {
            return;
        }

        var targetDirectory = GetModelDirectoryPath(identifier);
        if (Directory.Exists(targetDirectory))
        {
            DownloadStateHelper.RemoveAllDownloadStateFiles(sourceId, targetDirectory);

            var files = Directory.GetFiles(targetDirectory);
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
        }
    }
}