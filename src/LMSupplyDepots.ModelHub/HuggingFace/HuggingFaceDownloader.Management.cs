using LMSupplyDepots.ModelHub.Utils;
using LMSupplyDepots.Utils;
using System.Text.Json;

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

        var downloadStatus = await GetDownloadStatusAsync(sourceId, cancellationToken);
        if (downloadStatus != ModelDownloadStatus.Downloading)
        {
            _logger.LogWarning("Cannot pause download for model {ModelId} - not in downloading state", sourceId);
            return false;
        }

        try
        {
            var normalizedId = HuggingFaceHelper.NormalizeSourceId(sourceId);
            var modelType = await HuggingFaceHelper.DetermineModelTypeAsync(normalizedId, _client.Value, cancellationToken);
            var targetDirectory = _fileSystemRepository.GetModelDirectoryPath(sourceId, modelType);

            var pausedStatusFilePath = _fileSystemRepository.GetDownloadStatusFilePath(sourceId, modelType);

            long totalSize = 0;

            if (File.Exists(pausedStatusFilePath))
            {
                var content = await File.ReadAllTextAsync(pausedStatusFilePath, cancellationToken);
                if (long.TryParse(content.Trim(), out totalSize))
                {
                    var statusInfo = new
                    {
                        ModelId = sourceId,
                        Status = "Paused",
                        PausedAt = DateTime.UtcNow,
                        TargetDirectory = targetDirectory,
                        TotalSize = totalSize,
                        DownloadedSize = HuggingFaceHelper.CalculateDownloadedSize(targetDirectory)
                    };

                    var json = JsonHelper.Serialize(statusInfo);

                    await File.WriteAllTextAsync(pausedStatusFilePath, json, cancellationToken);
                    _logger.LogInformation("Download paused for model {ModelId}", sourceId);
                    return true;
                }
            }

            var newStatusInfo = new
            {
                ModelId = sourceId,
                Status = "Paused",
                PausedAt = DateTime.UtcNow,
                TargetDirectory = targetDirectory,
                DownloadedSize = HuggingFaceHelper.CalculateDownloadedSize(targetDirectory)
            };

            var newJson = JsonHelper.Serialize(newStatusInfo);

            await File.WriteAllTextAsync(pausedStatusFilePath, newJson, cancellationToken);
            _logger.LogInformation("Download paused for model {ModelId}", sourceId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pausing download for model {ModelId}", sourceId);
            return false;
        }
    }

    /// <summary>
    /// Cancels a download that is in progress or paused
    /// </summary>
    public async Task<bool> CancelDownloadAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Attempting to cancel download for model {ModelId}", sourceId);

        try
        {
            var normalizedId = HuggingFaceHelper.NormalizeSourceId(sourceId);
            var modelType = await HuggingFaceHelper.DetermineModelTypeAsync(normalizedId, _client.Value, cancellationToken);

            var statusFilePath = _fileSystemRepository.GetDownloadStatusFilePath(sourceId, modelType);
            if (File.Exists(statusFilePath))
            {
                File.Delete(statusFilePath);
                _logger.LogInformation("Removed download status file for model {ModelId}", sourceId);
            }

            var targetDirectory = _fileSystemRepository.GetModelDirectoryPath(sourceId, modelType);
            if (Directory.Exists(targetDirectory))
            {
                var files = Directory.GetFiles(targetDirectory);
                if (files.Length == 0 ||
                    files.All(f => Path.GetExtension(f) == ".download" ||
                               Path.GetExtension(f) == ".part" ||
                               Path.GetExtension(f) == ".tmp"))
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
            var normalizedId = HuggingFaceHelper.NormalizeSourceId(sourceId);

            string? artifactName = null;
            if (sourceId.Contains('/'))
            {
                var parts = sourceId.Split('/');
                if (parts.Length >= 3)
                {
                    artifactName = parts[parts.Length - 1];
                }
            }

            var existingModel = await _fileSystemRepository.GetModelAsync(sourceId, cancellationToken);
            if (existingModel != null && existingModel.IsLocal)
            {
                return ModelDownloadStatus.Completed;
            }

            foreach (var modelType in Enum.GetValues<ModelType>())
            {
                var modelDir = _fileSystemRepository.GetModelDirectoryPath(sourceId, modelType);

                if (Directory.Exists(modelDir))
                {
                    var statusFiles = Directory.GetFiles(modelDir, $"*{FileSystemHelper.DownloadStatusFileExtension}");

                    if (statusFiles.Length > 0)
                    {
                        string? statusFilePath = null;

                        if (!string.IsNullOrEmpty(artifactName))
                        {
                            var sanitizedArtifactName = artifactName.Replace(':', '_').Replace('/', '_');
                            var specificStatusFile = Path.Combine(modelDir, $"{sanitizedArtifactName}{FileSystemHelper.DownloadStatusFileExtension}");

                            if (File.Exists(specificStatusFile))
                            {
                                statusFilePath = specificStatusFile;
                            }
                        }

                        if (statusFilePath == null && statusFiles.Length > 0)
                        {
                            statusFilePath = statusFiles[0];
                        }

                        if (statusFilePath != null && File.Exists(statusFilePath))
                        {
                            return await ParseDownloadStatusFileAsync(statusFilePath, modelDir, cancellationToken);
                        }
                    }
                }

                var oldStatusFilePath = _fileSystemRepository.GetDownloadStatusFilePath(sourceId, modelType, artifactName);
                if (File.Exists(oldStatusFilePath))
                {
                    return await ParseDownloadStatusFileAsync(oldStatusFilePath, modelDir, cancellationToken);
                }
            }

            if (!string.IsNullOrEmpty(artifactName))
            {
                var downloadsDir = Path.Combine(_hubOptions.DataPath, FileSystemHelper.DownloadsDirectory);
                if (Directory.Exists(downloadsDir))
                {
                    var sanitizedArtifactName = artifactName.Replace(':', '_').Replace('/', '_');
                    var statusFile = Path.Combine(downloadsDir, $"{sanitizedArtifactName}{FileSystemHelper.DownloadStatusFileExtension}");

                    if (File.Exists(statusFile))
                    {
                        return await ParseDownloadStatusFileAsync(statusFile, null, cancellationToken);
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
    /// Parses a download status file to determine the download status
    /// </summary>
    private async Task<ModelDownloadStatus> ParseDownloadStatusFileAsync(
        string statusFilePath,
        string? targetDir = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var statusContent = await File.ReadAllTextAsync(statusFilePath, cancellationToken);

            if (statusContent.Contains("\"Status\"") &&
                statusContent.Contains("\"Paused\""))
            {
                return ModelDownloadStatus.Paused;
            }

            if (statusContent.Contains("\"Status\"") &&
                statusContent.Contains("\"Failed\""))
            {
                return ModelDownloadStatus.Failed;
            }

            if (statusContent.StartsWith("{") && statusContent.EndsWith("}"))
            {
                return ModelDownloadStatus.Downloading;
            }

            if (long.TryParse(statusContent.Trim(), out _))
            {
                if (!string.IsNullOrEmpty(targetDir) && Directory.Exists(targetDir))
                {
                    var recentFiles = Directory.GetFiles(targetDir, "*.*", SearchOption.AllDirectories)
                        .Where(f => (DateTime.UtcNow - new FileInfo(f).LastWriteTimeUtc).TotalSeconds < 30)
                        .Any();

                    return recentFiles ? ModelDownloadStatus.Downloading : ModelDownloadStatus.Paused;
                }

                return ModelDownloadStatus.Downloading;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error parsing download status file: {FilePath}", statusFilePath);
        }

        return ModelDownloadStatus.Paused;
    }
}