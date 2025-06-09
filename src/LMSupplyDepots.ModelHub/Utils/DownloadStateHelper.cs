namespace LMSupplyDepots.ModelHub.Utils;

/// <summary>
/// Simple download state information - just a marker file
/// </summary>
internal class DownloadState
{
    /// <summary>
    /// Model ID being downloaded
    /// </summary>
    public string ModelId { get; set; } = string.Empty;

    /// <summary>
    /// Total expected file size in bytes
    /// </summary>
    public long TotalSize { get; set; }

    /// <summary>
    /// When the download was started
    /// </summary>
    public DateTime StartedAt { get; set; }
}

/// <summary>
/// Simple download state management - marker files only
/// </summary>
internal static class DownloadStateHelper
{
    /// <summary>
    /// Creates a download state marker file
    /// </summary>
    public static async Task CreateDownloadStateFileAsync(
        string sourceId,
        string targetDirectory,
        string downloadingFileName,
        long totalSize,
        CancellationToken cancellationToken = default)
    {
        var downloadFilePath = Path.Combine(targetDirectory, $"{downloadingFileName}.download");
        var downloadState = new DownloadState
        {
            ModelId = sourceId,
            TotalSize = totalSize,
            StartedAt = DateTime.UtcNow
        };

        var json = JsonHelper.Serialize(downloadState);
        await File.WriteAllTextAsync(downloadFilePath, json, cancellationToken);
    }

    /// <summary>
    /// Loads download state from marker file
    /// </summary>
    public static DownloadState LoadFromFile(string downloadFilePath)
    {
        var json = File.ReadAllText(downloadFilePath);
        var state = JsonHelper.Deserialize<DownloadState>(json);
        return state ?? throw new InvalidOperationException($"Failed to deserialize download state from {downloadFilePath}");
    }

    /// <summary>
    /// Gets downloading filename from .download file path
    /// </summary>
    public static string GetDownloadingFileName(string downloadFilePath)
    {
        var fileName = Path.GetFileName(downloadFilePath);
        if (fileName.EndsWith(".download"))
        {
            return fileName[..^9]; // Remove ".download" extension
        }
        return fileName;
    }

    /// <summary>
    /// Gets real-time progress by comparing actual file size with expected size
    /// </summary>
    public static ModelDownloadProgress? GetRealTimeProgress(string sourceId, string dataPath)
    {
        try
        {
            var modelsPath = Path.Combine(dataPath, "models");
            if (!Directory.Exists(modelsPath)) return null;

            long totalDownloaded = 0;
            long totalSize = 0;
            string? fileName = null;
            DateTime? startedAt = null;

            var normalizedSourceId = sourceId.Replace("hf:", "").Replace('/', '_');

            foreach (var collectionDir in Directory.GetDirectories(modelsPath))
            {
                var collectionName = Path.GetFileName(collectionDir);
                if (!collectionName.Contains(normalizedSourceId.Split('_')[0])) continue;

                var downloadFiles = Directory.GetFiles(collectionDir, "*.download");
                foreach (var downloadFile in downloadFiles)
                {
                    try
                    {
                        var state = LoadFromFile(downloadFile);
                        if (state.ModelId != sourceId) continue;

                        var downloadingFileName = GetDownloadingFileName(downloadFile);
                        fileName ??= downloadingFileName;
                        startedAt ??= state.StartedAt;

                        // Check actual file size
                        var targetFile = Path.Combine(collectionDir, downloadingFileName);
                        var actualSize = File.Exists(targetFile) ? new FileInfo(targetFile).Length : 0;

                        totalDownloaded += actualSize;
                        totalSize += state.TotalSize;
                    }
                    catch { }
                }
            }

            if (totalSize > 0 || totalDownloaded > 0)
            {
                var status = (totalSize > 0 && totalDownloaded >= totalSize)
                    ? ModelDownloadStatus.Completed
                    : ModelDownloadStatus.Paused;

                return new ModelDownloadProgress
                {
                    ModelId = sourceId,
                    FileName = fileName ?? sourceId.Split('/').Last(),
                    BytesDownloaded = totalDownloaded,
                    TotalBytes = totalSize > 0 ? totalSize : null,
                    BytesPerSecond = 0,
                    Status = status,
                    StartedAt = startedAt
                };
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Checks if download is complete by comparing file sizes
    /// </summary>
    public static bool IsDownloadComplete(string targetDirectory, string sourceId)
    {
        if (!Directory.Exists(targetDirectory)) return false;

        var downloadFiles = Directory.GetFiles(targetDirectory, "*.download");
        if (downloadFiles.Length == 0)
        {
            return FileSystemHelper.ContainsModelFiles(targetDirectory);
        }

        foreach (var downloadFile in downloadFiles)
        {
            try
            {
                var state = LoadFromFile(downloadFile);
                if (state.ModelId != sourceId) continue;

                var downloadingFileName = GetDownloadingFileName(downloadFile);
                var expectedFile = Path.Combine(targetDirectory, downloadingFileName);
                if (!File.Exists(expectedFile)) return false;

                var actualSize = new FileInfo(expectedFile).Length;
                if (actualSize < state.TotalSize) return false; // Still downloading
            }
            catch
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Removes download state marker file
    /// </summary>
    public static void RemoveDownloadStateFile(string targetDirectory, string downloadingFileName)
    {
        var downloadFilePath = Path.Combine(targetDirectory, $"{downloadingFileName}.download");
        if (File.Exists(downloadFilePath))
        {
            try { File.Delete(downloadFilePath); } catch { }
        }
    }

    /// <summary>
    /// Removes all download state marker files for a model
    /// </summary>
    public static void RemoveAllDownloadStateFiles(string sourceId, string targetDirectory)
    {
        if (!Directory.Exists(targetDirectory)) return;

        var downloadFiles = Directory.GetFiles(targetDirectory, "*.download");
        foreach (var downloadFile in downloadFiles)
        {
            try
            {
                var state = LoadFromFile(downloadFile);
                if (state.ModelId == sourceId)
                {
                    File.Delete(downloadFile);
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Cleans up completed downloads by removing marker files
    /// </summary>
    public static void CleanupCompletedDownloads(string sourceId, string targetDirectory)
    {
        if (IsDownloadComplete(targetDirectory, sourceId))
        {
            RemoveAllDownloadStateFiles(sourceId, targetDirectory);
        }
    }

    /// <summary>
    /// Gets all download states for a source ID
    /// </summary>
    public static List<DownloadState> GetAllDownloadStates(string sourceId, string targetDirectory)
    {
        var states = new List<DownloadState>();
        if (!Directory.Exists(targetDirectory)) return states;

        var downloadFiles = Directory.GetFiles(targetDirectory, "*.download");
        foreach (var downloadFile in downloadFiles)
        {
            try
            {
                var state = LoadFromFile(downloadFile);
                if (state.ModelId == sourceId)
                {
                    states.Add(state);
                }
            }
            catch { }
        }

        return states;
    }

    /// <summary>
    /// Finds download states in data path
    /// </summary>
    public static List<DownloadState> FindDownloadStatesInDataPath(string sourceId, string dataPath)
    {
        var states = new List<DownloadState>();
        var modelsPath = Path.Combine(dataPath, "models");
        if (!Directory.Exists(modelsPath)) return states;

        foreach (var collectionDir in Directory.GetDirectories(modelsPath))
        {
            var downloadFiles = Directory.GetFiles(collectionDir, "*.download");
            foreach (var downloadFile in downloadFiles)
            {
                try
                {
                    var state = LoadFromFile(downloadFile);
                    if (string.IsNullOrEmpty(sourceId) || state.ModelId == sourceId)
                    {
                        states.Add(state);
                    }
                }
                catch { }
            }
        }

        return states;
    }

    /// <summary>
    /// Gets total progress for a source ID by checking actual file sizes
    /// </summary>
    public static (long downloaded, long total) GetTotalProgress(string sourceId, string targetDirectory)
    {
        var states = GetAllDownloadStates(sourceId, targetDirectory);
        long totalDownloaded = 0;
        long totalSize = 0;

        foreach (var state in states)
        {
            var downloadFile = Directory.GetFiles(targetDirectory, "*.download")
                .FirstOrDefault(f => LoadFromFile(f).ModelId == state.ModelId);

            if (downloadFile != null)
            {
                var downloadingFileName = GetDownloadingFileName(downloadFile);
                var expectedFile = Path.Combine(targetDirectory, downloadingFileName);
                if (File.Exists(expectedFile))
                {
                    var actualFileSize = new FileInfo(expectedFile).Length;
                    totalDownloaded += actualFileSize;
                }
            }
            totalSize += state.TotalSize;
        }

        return (totalDownloaded, totalSize);
    }
}