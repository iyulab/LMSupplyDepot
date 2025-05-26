using LMSupplyDepots.External.HuggingFace.Client;
using LMSupplyDepots.External.HuggingFace.Models;
using LMSupplyDepots.ModelHub.Utils;
using LMSupplyDepots.Utils;
using System.Net;
using System.Text.Json;

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
        _logger.LogInformation("Starting download of model {ModelId} to {TargetDir}", sourceId, targetDirectory);

        var (repoId, artifactName) = HuggingFaceHelper.NormalizeAndSplitSourceId(sourceId);

        Directory.CreateDirectory(targetDirectory);

        HuggingFaceModel hfModel;
        try
        {
            hfModel = await _client.Value.FindModelByRepoIdAsync(repoId, cancellationToken);
        }
        catch (HuggingFaceException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            var tokenProvided = !string.IsNullOrEmpty(_options.ApiToken);
            var message = tokenProvided
                ? $"The provided API token does not have sufficient permissions to access model '{repoId}'. This model may be private or gated."
                : $"Model '{repoId}' requires authentication. Please provide a HuggingFace API token.";

            _logger.LogError(ex, message);
            throw new ModelDownloadException(sourceId, message, ex);
        }

        var model = HuggingFaceHelper.ConvertToLMModel(hfModel);

        if (!string.IsNullOrEmpty(artifactName))
        {
            model.ArtifactName = artifactName;
            model.Id = $"{model.Registry}:{model.RepoId}/{artifactName}";
        }

        model.LocalPath = targetDirectory;

        var downloadFilePath = _fileSystemRepository.GetDownloadStatusFilePath(model.Id, model.Type, artifactName);

        long? totalSize;
        if (!string.IsNullOrEmpty(artifactName))
        {
            totalSize = HuggingFaceHelper.CalculateArtifactSize(hfModel, artifactName);
            _logger.LogInformation("Estimated size for artifact {ArtifactName}: {Size} bytes",
                artifactName, totalSize?.ToString() ?? "unknown");
        }
        else
        {
            totalSize = HuggingFaceHelper.CalculateTotalSize(hfModel);
            _logger.LogInformation("Estimated total repository size: {Size} bytes",
                totalSize?.ToString() ?? "unknown");
        }

        if (totalSize.HasValue)
        {
            await File.WriteAllTextAsync(downloadFilePath, totalSize.Value.ToString(), cancellationToken);
        }

        try
        {
            if (totalSize.HasValue && totalSize.Value > 0)
            {
                var availableSpace = GetAvailableDiskSpace(targetDirectory);
                if (availableSpace < totalSize.Value)
                {
                    throw new InsufficientDiskSpaceException(totalSize.Value, availableSpace);
                }
            }

            var downloadProgress = new Progress<External.HuggingFace.Download.RepoDownloadProgress>(p =>
            {
                var currentFile = p.CurrentProgresses.FirstOrDefault();
                var status = p.IsCompleted
                    ? ModelDownloadStatus.Completed
                    : ModelDownloadStatus.Downloading;

                long totalDownloadedBytes = 0;

                foreach (var completedFilePath in p.CompletedFiles)
                {
                    totalDownloadedBytes += HuggingFaceHelper.EstimateArtifactSize(
                        Path.GetFileNameWithoutExtension(completedFilePath),
                        Path.GetExtension(completedFilePath).TrimStart('.'));
                }

                totalDownloadedBytes += p.CurrentProgresses.Sum(f => f.CurrentBytes);

                progress?.Report(new ModelDownloadProgress
                {
                    ModelId = sourceId,
                    FileName = currentFile?.UploadPath ?? "Unknown",
                    BytesDownloaded = totalDownloadedBytes,
                    TotalBytes = totalSize,
                    BytesPerSecond = currentFile?.DownloadSpeed ?? 0,
                    EstimatedTimeRemaining = currentFile?.RemainingTime,
                    Status = status
                });
            });

            if (!string.IsNullOrEmpty(artifactName))
            {
                _logger.LogInformation("Downloading specific artifact: {ArtifactName} from repository {RepoId}",
                    artifactName, repoId);

                var filesToDownload = HuggingFaceHelper.FindArtifactFiles(hfModel, artifactName);

                if (filesToDownload.Count > 0)
                {
                    _logger.LogInformation("Found {Count} files for artifact {ArtifactName}: {Files}",
                        filesToDownload.Count, artifactName, string.Join(", ", filesToDownload));
                }
                else
                {
                    var exactFilename = $"{artifactName}.gguf";
                    _logger.LogInformation("No matching files found, trying exact filename: {Filename}", exactFilename);
                    filesToDownload.Add(exactFilename);
                }

                var subdir = Path.Combine(targetDirectory, repoId.Replace('/', '_'));
                var actualTargetDir = targetDirectory;

                await foreach (var _ in _client.Value.DownloadRepositoryFilesAsync(
                    repoId, filesToDownload, targetDirectory, false, cancellationToken))
                {
                }

                if (Directory.Exists(subdir))
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

                    try
                    {
                        Directory.Delete(subdir, true);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not delete subdirectory {SubDir}", subdir);
                    }
                }
            }
            else
            {
                _logger.LogInformation("Downloading entire repository: {RepoId}", repoId);
                await foreach (var _ in _client.Value.DownloadRepositoryAsync(
                    repoId, targetDirectory, false, cancellationToken))
                {
                }

                var subdir = Path.Combine(targetDirectory, repoId.Replace('/', '_'));
                if (Directory.Exists(subdir))
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

                    try
                    {
                        Directory.Delete(subdir, true);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not delete subdirectory {SubDir}", subdir);
                    }
                }
            }

            string? mainModelFile = null;
            if (model.Type == ModelType.TextGeneration || model.Type == ModelType.Embedding)
            {
                if (!string.IsNullOrEmpty(artifactName))
                {
                    var searchPattern = $"{artifactName}.*";
                    mainModelFile = Directory.GetFiles(targetDirectory, searchPattern, SearchOption.AllDirectories)
                        .FirstOrDefault(f => Path.GetExtension(f).TrimStart('.').ToLowerInvariant() is "gguf" or "bin" or "safetensors");

                    if (mainModelFile == null)
                    {
                        mainModelFile = Directory.GetFiles(targetDirectory, "*.*", SearchOption.AllDirectories)
                            .FirstOrDefault(f =>
                                (Path.GetExtension(f).TrimStart('.').ToLowerInvariant() is "gguf" or "bin" or "safetensors") &&
                                Path.GetFileNameWithoutExtension(f).Equals(artifactName, StringComparison.OrdinalIgnoreCase));
                    }

                    if (mainModelFile == null)
                    {
                        mainModelFile = Directory.GetFiles(targetDirectory, "*.*", SearchOption.AllDirectories)
                            .Where(f =>
                                (Path.GetExtension(f).TrimStart('.').ToLowerInvariant() is "gguf" or "bin" or "safetensors") &&
                                Path.GetFileNameWithoutExtension(f).Contains(artifactName, StringComparison.OrdinalIgnoreCase))
                            .OrderByDescending(f => new FileInfo(f).Length)
                            .FirstOrDefault();
                    }
                }

                if (mainModelFile == null)
                {
                    mainModelFile = Directory.GetFiles(targetDirectory, "*.gguf", SearchOption.AllDirectories)
                        .Concat(Directory.GetFiles(targetDirectory, "*.bin", SearchOption.AllDirectories))
                        .Concat(Directory.GetFiles(targetDirectory, "*.safetensors", SearchOption.AllDirectories))
                        .OrderByDescending(f => new FileInfo(f).Length)
                        .FirstOrDefault();
                }
            }

            if (mainModelFile != null)
            {
                model.Format = Path.GetExtension(mainModelFile).TrimStart('.');
                if (string.IsNullOrEmpty(model.ArtifactName) || !string.IsNullOrEmpty(artifactName))
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

            _logger.LogInformation("Completed download of model {ModelId}", sourceId);

            if (File.Exists(downloadFilePath))
            {
                try
                {
                    File.Delete(downloadFilePath);
                    _logger.LogDebug("Removed download status file: {FilePath}", downloadFilePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete download status file: {FilePath}", downloadFilePath);
                }
            }

            if (mainModelFile != null)
            {
                var modelDir = Path.GetDirectoryName(mainModelFile);
                if (!string.IsNullOrEmpty(modelDir) && Directory.Exists(modelDir))
                {
                    try
                    {
                        var statusFiles = Directory.GetFiles(modelDir, $"*{FileSystemHelper.DownloadStatusFileExtension}");
                        foreach (var statusFile in statusFiles)
                        {
                            File.Delete(statusFile);
                            _logger.LogDebug("Removed local status file: {FilePath}", statusFile);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to clean up status files in model directory: {Dir}", modelDir);
                    }
                }
            }

            return model;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading model {ModelId}: {Message}", sourceId, ex.Message);

            try
            {
                if (File.Exists(downloadFilePath))
                {
                    var statusInfo = new
                    {
                        ModelId = sourceId,
                        ArtifactName = artifactName,
                        Error = ex.Message,
                        ErrorTime = DateTime.UtcNow,
                        Status = "Failed"
                    };

                    var json = JsonHelper.Serialize(statusInfo);
                    await File.WriteAllTextAsync(downloadFilePath, json, cancellationToken);
                    _logger.LogDebug("Updated download status file with error information: {FilePath}", downloadFilePath);
                }
            }
            catch (Exception statusEx)
            {
                _logger.LogWarning(statusEx, "Failed to update download status file with error: {FilePath}", downloadFilePath);
            }

            if (ex is HuggingFaceException hfEx && hfEx.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new ModelDownloadException(sourceId,
                    "This model requires authentication. Please provide a valid HuggingFace API token.", ex);
            }

            throw new ModelDownloadException(sourceId, $"Download failed: {ex.Message}", ex);
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

            var statusFilePath = _fileSystemRepository.GetDownloadStatusFilePath(sourceId, modelType);
            string targetDirectory;

            if (File.Exists(statusFilePath))
            {
                var statusContent = await File.ReadAllTextAsync(statusFilePath, cancellationToken);

                try
                {
                    var statusInfo = JsonHelper.Deserialize<JsonElement>(statusContent);

                    if (statusInfo.TryGetProperty("TargetDirectory", out var targetDirElement))
                    {
                        targetDirectory = targetDirElement.GetString() ??
                            _fileSystemRepository.GetModelDirectoryPath(sourceId, modelType);
                    }
                    else
                    {
                        targetDirectory = _fileSystemRepository.GetModelDirectoryPath(sourceId, modelType);
                    }

                    if (statusInfo.TryGetProperty("TotalSize", out var totalSizeElement) &&
                        totalSizeElement.TryGetInt64(out var totalSize))
                    {
                        await File.WriteAllTextAsync(statusFilePath, totalSize.ToString(), cancellationToken);
                    }
                }
                catch
                {
                    targetDirectory = _fileSystemRepository.GetModelDirectoryPath(sourceId, modelType);
                }
            }
            else
            {
                targetDirectory = _fileSystemRepository.GetModelDirectoryPath(sourceId, modelType);
            }

            Directory.CreateDirectory(targetDirectory);

            return await DownloadModelAsync(sourceId, targetDirectory, progress, cancellationToken);
        }
        catch (Exception ex) when (!(ex is InvalidOperationException))
        {
            _logger.LogError(ex, "Error resuming download for model {ModelId}", sourceId);
            throw new InvalidOperationException($"Failed to resume download for model {sourceId}", ex);
        }
    }
}