using LMSupplyDepots.External.HuggingFace.Client;
using LMSupplyDepots.External.HuggingFace.Models;

namespace LMSupplyDepots.ModelHub.HuggingFace;

/// <summary>
/// Shared helper methods for HuggingFace downloader
/// </summary>
public partial class HuggingFaceDownloader
{
    /// <summary>
    /// Creates simple progress reporter
    /// </summary>
    protected IProgress<External.HuggingFace.Download.RepoDownloadProgress> CreateProgressReporter(
        string sourceId,
        string targetDirectory,
        long? totalSize,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (progress == null)
            return new Progress<External.HuggingFace.Download.RepoDownloadProgress>();

        return new Progress<External.HuggingFace.Download.RepoDownloadProgress>(p =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentFile = p.CurrentProgresses.FirstOrDefault();
            var fileName = currentFile?.UploadPath ?? GetFileNameFromSourceId(sourceId);
            var downloadedBytes = currentFile?.CurrentBytes ?? 0;
            var totalBytes = currentFile?.TotalBytes ?? totalSize;
            var downloadSpeed = currentFile?.DownloadSpeed ?? 0;
            var remainingTime = currentFile?.RemainingTime;
            var status = p.IsCompleted ? ModelDownloadStatus.Completed : ModelDownloadStatus.Downloading;

            progress.Report(new ModelDownloadProgress
            {
                ModelId = sourceId,
                FileName = fileName,
                BytesDownloaded = downloadedBytes,
                TotalBytes = totalBytes,
                BytesPerSecond = downloadSpeed,
                EstimatedTimeRemaining = remainingTime,
                Status = status,
                StartedAt = DateTime.UtcNow
            });
        });
    }

    /// <summary>
    /// Determines files to download based on artifact name
    /// </summary>
    protected List<string> GetFilesToDownload(HuggingFaceModel hfModel, string? artifactName, string sourceId)
    {
        if (!string.IsNullOrEmpty(artifactName))
        {
            var files = HuggingFaceHelper.FindArtifactFiles(hfModel, artifactName);
            if (files.Count == 0)
            {
                // Use helper method to ensure .gguf extension without duplication
                files.Add(HuggingFaceHelper.EnsureGgufExtension(artifactName));
            }
            return files;
        }

        var availableFiles = HuggingFaceHelper.GetAvailableModelFiles(hfModel);
        if (availableFiles.Count == 0)
        {
            throw new ModelNotFoundException(sourceId, "No model files found in repository");
        }
        return availableFiles;
    }

    /// <summary>
    /// Gets file name from source ID
    /// </summary>
    protected string GetFileNameFromSourceId(string sourceId)
    {
        if (sourceId.Contains('/'))
        {
            return sourceId.Split('/').Last();
        }
        return sourceId;
    }

    /// <summary>
    /// Gets HuggingFace model with error handling
    /// </summary>
    protected async Task<HuggingFaceModel> GetHuggingFaceModelAsync(string repoId, string sourceId, CancellationToken cancellationToken)
    {
        try
        {
            return await _client.FindModelByRepoIdAsync(repoId, cancellationToken);
        }
        catch (HuggingFaceException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            var message = string.IsNullOrEmpty(_options.ApiToken)
                ? $"Model '{repoId}' requires authentication. Please provide a HuggingFace API token."
                : $"Insufficient permissions to access model '{repoId}'. Check your API token.";

            throw new ModelDownloadException(sourceId, message, ex);
        }
    }

    /// <summary>
    /// Updates model with actual downloaded files
    /// </summary>
    protected Task UpdateModelWithActualFilesAsync(LMModel model, string targetDirectory, CancellationToken cancellationToken)
    {
        var mainModelFile = FileSystemHelper.FindMainModelFile(targetDirectory);
        if (mainModelFile != null)
        {
            var actualArtifactName = Path.GetFileNameWithoutExtension(mainModelFile);
            var format = Path.GetExtension(mainModelFile).TrimStart('.');
            var actualSize = new FileInfo(mainModelFile).Length;

            model.ArtifactName = actualArtifactName;
            model.Format = format;
            model.FilePaths = new List<string> { Path.GetFileName(mainModelFile) };
            model.SizeInBytes = actualSize;
            model.Id = $"{model.Registry}:{model.RepoId}/{actualArtifactName}";
        }
        else
        {
            var modelFiles = FileSystemHelper.GetModelFilesWithSizes(targetDirectory);
            if (modelFiles.Count > 0)
            {
                model.FilePaths = modelFiles.Keys.Select(Path.GetFileName).Where(name => !string.IsNullOrEmpty(name)).ToList()!;
                model.SizeInBytes = modelFiles.Values.Sum();
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Creates model metadata file
    /// </summary>
    protected Task CreateModelMetadataAsync(LMModel model, string targetDirectory, CancellationToken cancellationToken)
    {
        var mainModelFile = FileSystemHelper.FindMainModelFile(targetDirectory);
        if (mainModelFile != null)
        {
            var actualArtifactName = Path.GetFileNameWithoutExtension(mainModelFile);
            var metadataPath = Path.Combine(targetDirectory, $"{actualArtifactName}.json");
            var json = JsonHelper.Serialize(model);
            return File.WriteAllTextAsync(metadataPath, json, cancellationToken);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Checks available disk space
    /// </summary>
    protected void CheckDiskSpace(string targetDirectory, long requiredSize)
    {
        var driveInfo = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(targetDirectory)) ?? "C:\\");
        var availableSpace = driveInfo.AvailableFreeSpace;

        if (availableSpace < requiredSize)
        {
            throw new InsufficientDiskSpaceException(requiredSize, availableSpace);
        }
    }
}