using LMSupplyDepots.External.HuggingFace.Client;
using LMSupplyDepots.External.HuggingFace.Models;

namespace LMSupplyDepots.ModelHub.HuggingFace;

/// <summary>
/// Implementation of model download operations with accurate file size handling
/// </summary>
public partial class HuggingFaceDownloader
{
    /// <summary>
    /// Downloads a model from Hugging Face to local storage with proper cancellation support
    /// </summary>
    public async Task<LMModel> DownloadModelAsync(
        string sourceId,
        string targetDirectory,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting download of model {ModelId}", sourceId);

        var (repoId, artifactName) = HuggingFaceHelper.NormalizeAndSplitSourceId(sourceId);
        Directory.CreateDirectory(targetDirectory);

        // Create a combined cancellation token source for this download
        using var downloadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Register the cancellation token for pause functionality
        RegisterDownloadCancellationToken(sourceId, downloadCts);

        try
        {
            // Check if download is already complete
            if (DownloadStateHelper.IsDownloadComplete(targetDirectory, sourceId))
            {
                _logger.LogInformation("Model {ModelId} appears to be already downloaded", sourceId);
                DownloadStateHelper.CleanupCompletedDownloads(sourceId, targetDirectory);

                var existingModel = await TryLoadExistingModelAsync(sourceId, targetDirectory, downloadCts.Token);
                if (existingModel != null)
                {
                    return existingModel;
                }
            }

            // Get model info and actual file sizes
            var hfModel = await GetHuggingFaceModelAsync(repoId, sourceId, downloadCts.Token);
            var model = HuggingFaceHelper.ConvertToLMModel(hfModel);

            // Get actual file sizes from repository
            var repositoryFileSizes = await _client.Value.GetRepositoryFileSizesAsync(repoId, downloadCts.Token);

            // Determine what file(s) to download
            List<string> filesToDownload;
            if (!string.IsNullOrEmpty(artifactName))
            {
                model.ArtifactName = artifactName;
                model.Id = $"{model.Registry}:{model.RepoId}/{artifactName}";
                filesToDownload = HuggingFaceHelper.FindArtifactFiles(hfModel, artifactName);

                if (filesToDownload.Count == 0)
                {
                    var exactFilename = $"{artifactName}.gguf";
                    _logger.LogInformation("No matching files found, trying exact filename: {Filename}", exactFilename);
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

            // Create download state files with actual sizes
            var totalSize = 0L;
            var filesWithSizes = new Dictionary<string, long>();

            foreach (var file in filesToDownload)
            {
                var actualSize = HuggingFaceHelper.GetActualFileSize(repositoryFileSizes, file);

                if (!actualSize.HasValue)
                {
                    _logger.LogWarning("Could not determine actual size for {File}, skipping size validation", file);
                    actualSize = 0;
                }

                filesWithSizes[file] = actualSize.Value;
                totalSize += actualSize.Value;

                if (actualSize.Value > 0)
                {
                    await DownloadStateHelper.CreateDownloadStateFileAsync(
                        sourceId,
                        targetDirectory,
                        file,
                        actualSize.Value,
                        downloadCts.Token);
                }
            }

            // Check disk space with actual total size
            if (totalSize > 0)
            {
                CheckDiskSpace(targetDirectory, totalSize);
            }

            // Create progress reporter with actual total size
            var progressReporter = CreateProgressReporter(sourceId, targetDirectory, totalSize > 0 ? totalSize : null, progress);

            // Download files with proper cancellation
            if (!string.IsNullOrEmpty(artifactName))
            {
                await DownloadArtifactAsync(repoId, artifactName, targetDirectory, hfModel, progressReporter, downloadCts.Token);
            }
            else
            {
                await DownloadRepositoryAsync(repoId, targetDirectory, progressReporter, downloadCts.Token);
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

            _logger.LogInformation("Download completed for model {ModelId}", sourceId);
            return model;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Download was cancelled for model {ModelId}", sourceId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Download failed for model {ModelId}", sourceId);
            throw new ModelDownloadException(sourceId, ex.Message, ex);
        }
        finally
        {
            // Unregister the cancellation token
            UnregisterDownloadCancellationToken(sourceId);
        }
    }

    /// <summary>
    /// Tries to load an existing model from the target directory
    /// </summary>
    private async Task<LMModel?> TryLoadExistingModelAsync(string sourceId, string targetDirectory, CancellationToken cancellationToken)
    {
        try
        {
            var jsonFiles = Directory.GetFiles(targetDirectory, "*.json");
            if (jsonFiles.Length > 0)
            {
                var json = await File.ReadAllTextAsync(jsonFiles[0], cancellationToken);
                var model = JsonHelper.Deserialize<LMModel>(json);
                if (model != null && model.Id == sourceId)
                {
                    _logger.LogInformation("Found existing model metadata for {ModelId}", sourceId);
                    return model;
                }
            }

            var mainModelFile = FileSystemHelper.FindMainModelFile(targetDirectory);
            if (mainModelFile != null)
            {
                _logger.LogInformation("Reconstructing model metadata from existing files for {ModelId}", sourceId);

                var (repoId, artifactName) = HuggingFaceHelper.NormalizeAndSplitSourceId(sourceId);
                var hfModel = await GetHuggingFaceModelAsync(repoId, sourceId, cancellationToken);
                var model = HuggingFaceHelper.ConvertToLMModel(hfModel);

                model.LocalPath = targetDirectory;
                await UpdateModelWithActualFilesAsync(model, targetDirectory, cancellationToken);
                await CreateModelMetadataAsync(model, targetDirectory, cancellationToken);

                return model;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load existing model for {ModelId}", sourceId);
        }

        return null;
    }

    private async Task<HuggingFaceModel> GetHuggingFaceModelAsync(string repoId, string sourceId, CancellationToken cancellationToken)
    {
        try
        {
            return await _client.Value.FindModelByRepoIdAsync(repoId, cancellationToken);
        }
        catch (HuggingFaceException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            var message = string.IsNullOrEmpty(_options.ApiToken)
                ? $"Model '{repoId}' requires authentication. Please provide a HuggingFace API token."
                : $"Insufficient permissions to access model '{repoId}'. Check your API token.";

            throw new ModelDownloadException(sourceId, message, ex);
        }
    }

    private void CheckDiskSpace(string targetDirectory, long requiredSize)
    {
        var availableSpace = GetAvailableDiskSpace(targetDirectory);
        if (availableSpace < requiredSize)
        {
            throw new InsufficientDiskSpaceException(requiredSize, availableSpace);
        }
    }

    private IProgress<External.HuggingFace.Download.RepoDownloadProgress> CreateProgressReporter(
        string sourceId,
        string targetDirectory,
        long? totalSize,
        IProgress<ModelDownloadProgress>? progress)
    {
        if (progress == null)
            return new Progress<External.HuggingFace.Download.RepoDownloadProgress>();

        return new Progress<External.HuggingFace.Download.RepoDownloadProgress>(p =>
        {
            var (downloadedSize, actualTotalSize) = DownloadStateHelper.GetTotalProgress(sourceId, targetDirectory);
            var currentFile = p.CurrentProgresses.FirstOrDefault();

            // Use actual total size if available, otherwise fall back to provided total size
            var effectiveTotalSize = actualTotalSize > 0 ? actualTotalSize : totalSize;

            // Ensure downloaded size doesn't exceed total size for progress calculation
            var effectiveDownloadedSize = effectiveTotalSize.HasValue
                ? Math.Min(downloadedSize, effectiveTotalSize.Value)
                : downloadedSize;

            progress.Report(new ModelDownloadProgress
            {
                ModelId = sourceId,
                FileName = currentFile?.UploadPath ?? GetFileNameFromSourceId(sourceId),
                BytesDownloaded = effectiveDownloadedSize,
                TotalBytes = effectiveTotalSize,
                BytesPerSecond = currentFile?.DownloadSpeed ?? 0,
                EstimatedTimeRemaining = currentFile?.RemainingTime,
                Status = p.IsCompleted ? ModelDownloadStatus.Completed : ModelDownloadStatus.Downloading
            });
        });
    }

    private string GetFileNameFromSourceId(string sourceId)
    {
        if (sourceId.Contains('/'))
        {
            return sourceId.Split('/').Last();
        }
        return sourceId;
    }

    private async Task DownloadArtifactAsync(
        string repoId,
        string artifactName,
        string targetDirectory,
        HuggingFaceModel hfModel,
        IProgress<External.HuggingFace.Download.RepoDownloadProgress> progress,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Downloading specific artifact: {ArtifactName} from repository {RepoId}",
            artifactName, repoId);

        var filesToDownload = HuggingFaceHelper.FindArtifactFiles(hfModel, artifactName);
        if (filesToDownload.Count == 0)
        {
            var exactFilename = $"{artifactName}.gguf";
            _logger.LogInformation("No matching files found, trying exact filename: {Filename}", exactFilename);
            filesToDownload.Add(exactFilename);
        }

        // Download each file with resume support
        foreach (var file in filesToDownload)
        {
            await DownloadSingleFileWithResumeAsync(repoId, file, targetDirectory, cancellationToken);
        }

        MoveFilesToTargetDirectory(targetDirectory, repoId);
    }

    private async Task DownloadRepositoryAsync(
        string repoId,
        string targetDirectory,
        IProgress<External.HuggingFace.Download.RepoDownloadProgress> progress,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Downloading entire repository: {RepoId}", repoId);

        await foreach (var _ in _client.Value.DownloadRepositoryAsync(
            repoId, targetDirectory, false, cancellationToken))
        {
            // Progress is handled by the progress reporter
        }

        MoveFilesToTargetDirectory(targetDirectory, repoId);
    }

    /// <summary>
    /// Downloads a single file with automatic resume support and proper progress tracking
    /// </summary>
    private async Task DownloadSingleFileWithResumeAsync(
        string repoId,
        string fileName,
        string targetDirectory,
        CancellationToken cancellationToken)
    {
        var outputPath = Path.Combine(targetDirectory, fileName);

        // Check if file already exists and get current size
        long startFrom = 0;
        if (File.Exists(outputPath))
        {
            var fileInfo = new FileInfo(outputPath);
            startFrom = fileInfo.Length;

            if (startFrom > 0)
            {
                _logger.LogInformation("Resuming {FileName} from byte position {StartFrom}", fileName, startFrom);
            }
        }

        try
        {
            // Create progress wrapper that properly handles byte counting for resume
            var progressWrapper = new Progress<External.HuggingFace.Download.FileDownloadProgress>(p =>
            {
                // For resumed downloads, CurrentBytes from External client is the amount downloaded in this session
                // We need to add the startFrom offset to get the total bytes downloaded
                var totalBytesDownloaded = startFrom + p.CurrentBytes;

                // But if the client is reporting absolute position, use that instead
                var actualBytesDownloaded = p.CurrentBytes >= startFrom ? p.CurrentBytes : totalBytesDownloaded;

                _logger.LogDebug("File progress for {FileName}: {Current}/{Total} bytes (startFrom: {StartFrom})",
                    fileName, actualBytesDownloaded, p.TotalBytes, startFrom);
            });

            var result = await _client.Value.DownloadFileWithResultAsync(
                repoId,
                fileName,
                outputPath,
                startFrom, // Resume from current position
                progressWrapper,
                cancellationToken);

            _logger.LogInformation("File {FileName} download completed, total file size: {TotalBytes} bytes",
                fileName, new FileInfo(outputPath).Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download file {FileName}", fileName);
            throw;
        }
    }

    private void MoveFilesToTargetDirectory(string targetDirectory, string repoId)
    {
        var subdir = Path.Combine(targetDirectory, repoId.Replace('/', '_'));
        if (!Directory.Exists(subdir))
            return;

        try
        {
            _logger.LogInformation("Moving files from subdirectory {SubDir} to {TargetDir}", subdir, targetDirectory);

            foreach (var file in Directory.GetFiles(subdir, "*.*", SearchOption.AllDirectories))
            {
                var fileName = Path.GetFileName(file);
                var destinationPath = Path.Combine(targetDirectory, fileName);

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

                if (File.Exists(destinationPath))
                    File.Delete(destinationPath);

                File.Move(file, destinationPath);
            }

            Directory.Delete(subdir, true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to move files from subdirectory {SubDir}", subdir);
        }
    }

    /// <summary>
    /// Updates model information with actual downloaded files and sizes
    /// </summary>
    private async Task UpdateModelWithActualFilesAsync(LMModel model, string targetDirectory, CancellationToken cancellationToken)
    {
        var mainModelFile = FileSystemHelper.FindMainModelFile(targetDirectory);
        if (mainModelFile != null)
        {
            var actualArtifactName = Path.GetFileNameWithoutExtension(mainModelFile);
            var format = Path.GetExtension(mainModelFile).TrimStart('.');
            var actualSize = new FileInfo(mainModelFile).Length;

            // Update model with actual file information
            model.ArtifactName = actualArtifactName;
            model.Format = format;
            model.FilePaths = new List<string> { Path.GetFileName(mainModelFile) };
            model.SizeInBytes = actualSize;

            // Update the model ID to reflect the actual artifact name
            model.Id = $"{model.Registry}:{model.RepoId}/{actualArtifactName}";

            _logger.LogInformation("Updated model info with actual file: {ArtifactName}.{Format}, Size: {Size} bytes",
                actualArtifactName, format, actualSize);
        }
        else
        {
            // Handle multiple files case
            var modelFiles = FileSystemHelper.GetModelFilesWithSizes(targetDirectory);
            if (modelFiles.Count > 0)
            {
                var totalSize = modelFiles.Values.Sum();
                var fileNames = modelFiles.Keys.Select(Path.GetFileName).ToList();

                model.FilePaths = fileNames;
                model.SizeInBytes = totalSize;

                _logger.LogInformation("Updated model info with {FileCount} files, Total size: {Size} bytes",
                    fileNames.Count, totalSize);
            }
        }
    }

    private async Task CreateModelMetadataAsync(LMModel model, string targetDirectory, CancellationToken cancellationToken)
    {
        string? mainModelFile = null;

        if (model.Type == ModelType.TextGeneration || model.Type == ModelType.Embedding)
        {
            mainModelFile = FileSystemHelper.FindMainModelFile(targetDirectory);
        }

        if (mainModelFile != null)
        {
            var actualArtifactName = Path.GetFileNameWithoutExtension(mainModelFile);
            var format = Path.GetExtension(mainModelFile).TrimStart('.');
            var actualSize = new FileInfo(mainModelFile).Length;

            // Ensure model properties match the actual file
            model.Format = format;
            model.ArtifactName = actualArtifactName;
            model.FilePaths = new List<string> { Path.GetFileName(mainModelFile) };
            model.SizeInBytes = actualSize;

            // Use actual artifact name for metadata file
            var metadataPath = Path.Combine(targetDirectory, $"{actualArtifactName}.json");

            var json = JsonHelper.Serialize(model);
            await File.WriteAllTextAsync(metadataPath, json, cancellationToken);

            _logger.LogInformation("Created metadata file: {MetadataPath}", metadataPath);
        }
        else
        {
            _logger.LogWarning("No main model file found in {TargetDirectory}", targetDirectory);
        }
    }

    /// <summary>
    /// Gets available disk space for a path
    /// </summary>
    private static long GetAvailableDiskSpace(string path)
    {
        var driveInfo = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path)) ?? "C:\\");
        return driveInfo.AvailableFreeSpace;
    }
}