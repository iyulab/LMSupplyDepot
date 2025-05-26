using LMSupplyDepots.External.HuggingFace.Client;
using LMSupplyDepots.External.HuggingFace.Models;

namespace LMSupplyDepots.ModelHub.HuggingFace;

/// <summary>
/// Implementation of model download operations
/// </summary>
public partial class HuggingFaceDownloader
{
    /// <summary>
    /// Downloads a model from Hugging Face to local storage
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

        // Get model info
        var hfModel = await GetHuggingFaceModelAsync(repoId, sourceId, cancellationToken);
        var model = HuggingFaceHelper.ConvertToLMModel(hfModel);

        if (!string.IsNullOrEmpty(artifactName))
        {
            model.ArtifactName = artifactName;
            model.Id = $"{model.Registry}:{model.RepoId}/{artifactName}";
        }
        model.LocalPath = targetDirectory;

        // Calculate total size and create status file
        var totalSize = CalculateTotalSize(hfModel, artifactName);
        if (totalSize.HasValue)
        {
            await DownloadStatusHelper.CreateStatusFileAsync(sourceId, totalSize.Value, _hubOptions.DataPath);
            CheckDiskSpace(targetDirectory, totalSize.Value);
        }

        try
        {
            // Create progress reporter
            var progressReporter = CreateProgressReporter(sourceId, targetDirectory, totalSize, progress);

            // Download files
            if (!string.IsNullOrEmpty(artifactName))
            {
                await DownloadArtifactAsync(repoId, artifactName, targetDirectory, hfModel, progressReporter, cancellationToken);
            }
            else
            {
                await DownloadRepositoryAsync(repoId, targetDirectory, progressReporter, cancellationToken);
            }

            // Create metadata
            await CreateModelMetadataAsync(model, targetDirectory, cancellationToken);

            // Remove status file on success
            DownloadStatusHelper.RemoveStatusFile(sourceId, _hubOptions.DataPath);

            _logger.LogInformation("Download completed for model {ModelId}", sourceId);
            return model;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Download failed for model {ModelId}", sourceId);
            throw new ModelDownloadException(sourceId, ex.Message, ex);
        }
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

    private long? CalculateTotalSize(HuggingFaceModel hfModel, string? artifactName)
    {
        return !string.IsNullOrEmpty(artifactName)
            ? HuggingFaceHelper.CalculateArtifactSize(hfModel, artifactName)
            : HuggingFaceHelper.CalculateTotalSize(hfModel);
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
            var downloadedSize = targetDirectory.GetModelFilesSize();
            var currentFile = p.CurrentProgresses.FirstOrDefault();

            progress.Report(new ModelDownloadProgress
            {
                ModelId = sourceId,
                FileName = currentFile?.UploadPath ?? sourceId.GetFileNameFromSourceId(),
                BytesDownloaded = downloadedSize,
                TotalBytes = totalSize,
                BytesPerSecond = currentFile?.DownloadSpeed ?? 0,
                EstimatedTimeRemaining = currentFile?.RemainingTime,
                Status = p.IsCompleted ? ModelDownloadStatus.Completed : ModelDownloadStatus.Downloading
            });
        });
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

        await foreach (var _ in _client.Value.DownloadRepositoryFilesAsync(
            repoId, filesToDownload, targetDirectory, false, cancellationToken))
        {
            // Progress is handled by the progress reporter
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

    private async Task CreateModelMetadataAsync(LMModel model, string targetDirectory, CancellationToken cancellationToken)
    {
        string? mainModelFile = null;

        if (model.Type == ModelType.TextGeneration || model.Type == ModelType.Embedding)
        {
            mainModelFile = FileSystemHelper.FindMainModelFile(targetDirectory);
        }

        if (mainModelFile != null)
        {
            model.Format = Path.GetExtension(mainModelFile).TrimStart('.');
            if (string.IsNullOrEmpty(model.ArtifactName))
            {
                model.ArtifactName = Path.GetFileNameWithoutExtension(mainModelFile);
            }
            model.FilePaths = new List<string> { Path.GetFileName(mainModelFile) };
            model.SizeInBytes = new FileInfo(mainModelFile).Length;

            var modelBaseName = Path.GetFileNameWithoutExtension(mainModelFile);
            var metadataPath = Path.Combine(Path.GetDirectoryName(mainModelFile) ?? targetDirectory, $"{modelBaseName}.json");

            var json = JsonHelper.Serialize(model);
            await File.WriteAllTextAsync(metadataPath, json, cancellationToken);

            _logger.LogInformation("Created metadata file: {MetadataPath}", metadataPath);
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