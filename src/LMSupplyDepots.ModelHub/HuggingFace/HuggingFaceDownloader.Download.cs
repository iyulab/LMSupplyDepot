using LMSupplyDepots.External.HuggingFace.Client;
using LMSupplyDepots.External.HuggingFace.Models;

namespace LMSupplyDepots.ModelHub.HuggingFace;

/// <summary>
/// Implementation of model download operations with enhanced cancellation handling
/// </summary>
public partial class HuggingFaceDownloader
{
    /// <summary>
    /// Downloads a model from Hugging Face to local storage with enhanced cancellation
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

        try
        {
            // Check if download is already complete
            if (DownloadStateHelper.IsDownloadComplete(targetDirectory, sourceId))
            {
                _logger.LogInformation("Model {ModelId} appears to be already downloaded", sourceId);
                DownloadStateHelper.CleanupCompletedDownloads(sourceId, targetDirectory);

                var existingModel = await TryLoadExistingModelAsync(sourceId, targetDirectory, cancellationToken);
                if (existingModel != null)
                {
                    return existingModel;
                }
            }

            // Get model info and actual file sizes
            var hfModel = await GetHuggingFaceModelAsync(repoId, sourceId, cancellationToken);
            var model = HuggingFaceHelper.ConvertToLMModel(hfModel);

            // Get actual file sizes from repository
            var repositoryFileSizes = await _client.GetRepositoryFileSizesAsync(repoId, cancellationToken);

            // Determine what file(s) to download
            var filesToDownload = GetFilesToDownload(hfModel, artifactName, sourceId);
            model.LocalPath = targetDirectory;

            // Create download state files with actual sizes
            var totalSize = await CreateDownloadStateFilesAsync(sourceId, targetDirectory, filesToDownload, repositoryFileSizes, cancellationToken);

            // Check disk space with actual total size
            if (totalSize > 0)
            {
                CheckDiskSpace(targetDirectory, totalSize);
            }

            // Create progress reporter with frequent cancellation checks
            var progressReporter = CreateProgressReporter(sourceId, targetDirectory, totalSize > 0 ? totalSize : null, progress, cancellationToken);

            // Download files with enhanced cancellation
            if (!string.IsNullOrEmpty(artifactName))
            {
                await DownloadArtifactWithCancellationAsync(repoId, artifactName, targetDirectory, hfModel, progressReporter, cancellationToken);
            }
            else
            {
                await DownloadRepositoryWithCancellationAsync(repoId, targetDirectory, progressReporter, cancellationToken);
            }

            // Update model with actual downloaded file info
            await UpdateModelWithActualFilesAsync(model, targetDirectory, cancellationToken);

            // Create metadata
            await CreateModelMetadataAsync(model, targetDirectory, cancellationToken);

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
    }

    /// <summary>
    /// Downloads artifact with cancellation checking
    /// </summary>
    private async Task DownloadArtifactWithCancellationAsync(
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
            // Use helper method to ensure .gguf extension without duplication
            var exactFilename = HuggingFaceHelper.EnsureGgufExtension(artifactName);
            _logger.LogInformation("No matching files found, trying exact filename: {Filename}", exactFilename);
            filesToDownload.Add(exactFilename);
        }

        // Download each file with cancellation checks
        foreach (var file in filesToDownload)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await DownloadSingleFileAsync(repoId, file, targetDirectory, cancellationToken);
        }

        MoveFilesToTargetDirectory(targetDirectory, repoId);
    }

    /// <summary>
    /// Downloads repository with cancellation checking
    /// </summary>
    private async Task DownloadRepositoryWithCancellationAsync(
        string repoId,
        string targetDirectory,
        IProgress<External.HuggingFace.Download.RepoDownloadProgress> progress,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Downloading entire repository: {RepoId}", repoId);

        await foreach (var _ in _client.DownloadRepositoryAsync(
            repoId, targetDirectory, false, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        MoveFilesToTargetDirectory(targetDirectory, repoId);
    }

    /// <summary>
    /// Downloads a single file with cancellation support
    /// </summary>
    private async Task DownloadSingleFileAsync(
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
            // Create progress wrapper with cancellation checks
            var progressWrapper = new Progress<External.HuggingFace.Download.FileDownloadProgress>(p =>
            {
                cancellationToken.ThrowIfCancellationRequested();
            });

            var result = await _client.DownloadFileWithResultAsync(
                repoId,
                fileName,
                outputPath,
                startFrom,
                progressWrapper,
                cancellationToken);

            _logger.LogInformation("File {FileName} download completed, total file size: {TotalBytes} bytes",
                fileName, new FileInfo(outputPath).Length);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("File {FileName} download was cancelled", fileName);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download file {FileName}", fileName);
            throw;
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

    /// <summary>
    /// Creates download state files with actual file sizes
    /// </summary>
    private async Task<long> CreateDownloadStateFilesAsync(
        string sourceId,
        string targetDirectory,
        List<string> filesToDownload,
        Dictionary<string, long> repositoryFileSizes,
        CancellationToken cancellationToken)
    {
        var totalSize = 0L;
        foreach (var file in filesToDownload)
        {
            var actualSize = HuggingFaceHelper.GetActualFileSize(repositoryFileSizes, file);
            if (!actualSize.HasValue)
            {
                _logger.LogWarning("Could not determine actual size for {File}, skipping size validation", file);
                actualSize = 0;
            }

            totalSize += actualSize.Value;

            if (actualSize.Value > 0)
            {
                await DownloadStateHelper.CreateDownloadStateFileAsync(
                    sourceId,
                    targetDirectory,
                    file,
                    actualSize.Value,
                    cancellationToken);
            }
        }

        return totalSize;
    }

    /// <summary>
    /// Moves files from subdirectory to target directory
    /// </summary>
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
}