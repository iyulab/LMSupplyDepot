namespace LMSupplyDepots.ModelHub.Utils;

/// <summary>
/// Helper for managing download state files with improved accuracy
/// </summary>
internal static class DownloadStateHelper
{
    private const double CompletionThreshold = 1.0; // Require 100% completion

    /// <summary>
    /// Creates a download state file with actual file size
    /// </summary>
    public static async Task CreateDownloadStateFileAsync(
        string sourceId,
        string targetDirectory,
        string downloadingFileName,
        long actualTotalSize,
        CancellationToken cancellationToken = default)
    {
        if (!ModelIdentifier.TryParse(sourceId, out var identifier))
        {
            throw new ArgumentException($"Invalid source ID format: {sourceId}", nameof(sourceId));
        }

        var downloadFilePath = Path.Combine(targetDirectory, $"{downloadingFileName}.download");

        var downloadState = new DownloadState
        {
            ModelId = sourceId,
            DownloadingFileName = downloadingFileName,
            ArtifactName = identifier.ArtifactName,
            Format = identifier.Format,
            TargetDirectory = targetDirectory,
            TotalSize = actualTotalSize,
            StartedAt = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow
        };

        var json = JsonHelper.Serialize(downloadState);
        await File.WriteAllTextAsync(downloadFilePath, json, cancellationToken);
    }

    /// <summary>
    /// Updates download progress atomically
    /// </summary>
    public static async Task UpdateDownloadProgressAsync(
        string targetDirectory,
        string downloadingFileName,
        long downloadedBytes,
        CancellationToken cancellationToken = default)
    {
        var downloadFilePath = Path.Combine(targetDirectory, $"{downloadingFileName}.download");

        if (!File.Exists(downloadFilePath))
        {
            return;
        }

        try
        {
            var state = LoadFromFile(downloadFilePath);
            state.DownloadedBytes = downloadedBytes;
            state.LastUpdated = DateTime.UtcNow;

            var json = JsonHelper.Serialize(state);
            var tempFilePath = downloadFilePath + ".tmp";

            await File.WriteAllTextAsync(tempFilePath, json, cancellationToken);
            File.Move(tempFilePath, downloadFilePath, true);
        }
        catch (Exception)
        {
            // Ignore update errors - state file might be corrupted or in use
        }
    }

    /// <summary>
    /// Removes download state file by filename
    /// </summary>
    public static void RemoveDownloadStateFile(string targetDirectory, string downloadingFileName)
    {
        var downloadFilePath = Path.Combine(targetDirectory, $"{downloadingFileName}.download");

        if (File.Exists(downloadFilePath))
        {
            try
            {
                File.Delete(downloadFilePath);
            }
            catch
            {
                // Ignore deletion errors
            }
        }
    }

    /// <summary>
    /// Removes all download state files for a source ID
    /// </summary>
    public static void RemoveAllDownloadStateFiles(string sourceId, string targetDirectory)
    {
        if (!Directory.Exists(targetDirectory))
            return;

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
            catch
            {
                // Continue to next file if this one is corrupted
            }
        }
    }

    /// <summary>
    /// Loads download state from file with validation
    /// </summary>
    public static DownloadState LoadFromFile(string downloadFilePath)
    {
        var json = File.ReadAllText(downloadFilePath);
        var state = JsonHelper.Deserialize<DownloadState>(json);

        if (state == null)
            throw new InvalidOperationException($"Failed to deserialize download state from {downloadFilePath}");

        return state;
    }

    /// <summary>
    /// Checks if download is complete based on actual file verification
    /// </summary>
    public static bool IsDownloadComplete(string targetDirectory, string sourceId)
    {
        if (!Directory.Exists(targetDirectory))
            return false;

        var downloadFiles = Directory.GetFiles(targetDirectory, "*.download");
        if (downloadFiles.Length == 0)
        {
            return HasValidModelFiles(targetDirectory);
        }

        foreach (var downloadFile in downloadFiles)
        {
            try
            {
                var state = LoadFromFile(downloadFile);
                if (state.ModelId != sourceId)
                    continue;

                var expectedFile = Path.Combine(targetDirectory, state.DownloadingFileName);
                if (!File.Exists(expectedFile))
                    return false;

                var actualSize = new FileInfo(expectedFile).Length;
                var expectedSize = state.TotalSize;

                // Require exact size match for completion
                if (actualSize != expectedSize)
                    return false;

                // Verify file integrity if possible
                if (!IsFileIntegrityValid(expectedFile))
                    return false;
            }
            catch
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Gets all download states for a source ID
    /// </summary>
    public static List<DownloadState> GetAllDownloadStates(string sourceId, string targetDirectory)
    {
        var states = new List<DownloadState>();

        if (!Directory.Exists(targetDirectory))
            return states;

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
            catch
            {
                // Skip corrupted files
            }
        }

        return states;
    }

    /// <summary>
    /// Calculates total download progress for all files with proper size validation
    /// </summary>
    public static (long downloaded, long total) GetTotalProgress(string sourceId, string targetDirectory)
    {
        var states = GetAllDownloadStates(sourceId, targetDirectory);

        if (states.Count == 0)
            return (0, 0);

        long totalDownloaded = 0;
        long totalSize = 0;

        foreach (var state in states)
        {
            var expectedFile = Path.Combine(targetDirectory, state.DownloadingFileName);
            if (File.Exists(expectedFile))
            {
                var actualFileSize = new FileInfo(expectedFile).Length;
                var expectedTotalSize = state.TotalSize;

                // Use the actual file size, but don't exceed the expected total size
                var effectiveDownloadedSize = expectedTotalSize > 0
                    ? Math.Min(actualFileSize, expectedTotalSize)
                    : actualFileSize;

                totalDownloaded += effectiveDownloadedSize;
            }

            totalSize += state.TotalSize;
        }

        return (totalDownloaded, totalSize);
    }

    /// <summary>
    /// Gets accurate progress for a specific file
    /// </summary>
    public static (long downloaded, long total) GetFileProgress(string sourceId, string targetDirectory, string fileName)
    {
        var states = GetAllDownloadStates(sourceId, targetDirectory);
        var fileState = states.FirstOrDefault(s => s.DownloadingFileName == fileName);

        if (fileState == null)
            return (0, 0);

        var expectedFile = Path.Combine(targetDirectory, fileName);
        if (!File.Exists(expectedFile))
            return (0, fileState.TotalSize);

        var actualFileSize = new FileInfo(expectedFile).Length;
        var expectedTotalSize = fileState.TotalSize;

        // Ensure downloaded size doesn't exceed total size
        var effectiveDownloadedSize = expectedTotalSize > 0
            ? Math.Min(actualFileSize, expectedTotalSize)
            : actualFileSize;

        return (effectiveDownloadedSize, expectedTotalSize);
    }

    /// <summary>
    /// Cleans up completed downloads atomically
    /// </summary>
    public static void CleanupCompletedDownloads(string sourceId, string targetDirectory)
    {
        if (!Directory.Exists(targetDirectory))
            return;

        if (IsDownloadComplete(targetDirectory, sourceId))
        {
            RemoveAllDownloadStateFiles(sourceId, targetDirectory);
        }
    }

    /// <summary>
    /// Validates download state consistency with improved size checking
    /// </summary>
    public static bool ValidateDownloadState(string sourceId, string targetDirectory)
    {
        var states = GetAllDownloadStates(sourceId, targetDirectory);

        foreach (var state in states)
        {
            var expectedFile = Path.Combine(targetDirectory, state.DownloadingFileName);

            // Check if file exists
            if (!File.Exists(expectedFile))
                continue;

            var actualSize = new FileInfo(expectedFile).Length;
            var expectedSize = state.TotalSize;

            // Check if file size is reasonable
            if (expectedSize > 0 && actualSize > expectedSize * 1.1) // Allow 10% tolerance
            {
                // File is significantly larger than expected - possibly corrupted state
                return false;
            }

            // Update downloaded bytes if needed
            var effectiveDownloadedSize = expectedSize > 0
                ? Math.Min(actualSize, expectedSize)
                : actualSize;

            if (state.DownloadedBytes != effectiveDownloadedSize)
            {
                state.DownloadedBytes = effectiveDownloadedSize;
                state.LastUpdated = DateTime.UtcNow;

                var downloadFilePath = Path.Combine(targetDirectory, $"{state.DownloadingFileName}.download");
                try
                {
                    var json = JsonHelper.Serialize(state);
                    File.WriteAllText(downloadFilePath, json);
                }
                catch
                {
                    // Ignore write errors
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Gets the completion percentage for a download with proper bounds checking
    /// </summary>
    public static double GetCompletionPercentage(string sourceId, string targetDirectory)
    {
        var (downloaded, total) = GetTotalProgress(sourceId, targetDirectory);

        if (total <= 0)
            return 0.0;

        var percentage = (double)downloaded / total;
        return Math.Max(0.0, Math.Min(1.0, percentage));
    }

    /// <summary>
    /// Synchronizes download state with actual file sizes
    /// </summary>
    public static async Task SynchronizeDownloadStateAsync(string sourceId, string targetDirectory, CancellationToken cancellationToken = default)
    {
        var states = GetAllDownloadStates(sourceId, targetDirectory);

        foreach (var state in states)
        {
            var expectedFile = Path.Combine(targetDirectory, state.DownloadingFileName);
            if (File.Exists(expectedFile))
            {
                var actualSize = new FileInfo(expectedFile).Length;
                var expectedSize = state.TotalSize;

                // Update state with actual file size (capped at expected size)
                var effectiveDownloadedSize = expectedSize > 0
                    ? Math.Min(actualSize, expectedSize)
                    : actualSize;

                if (state.DownloadedBytes != effectiveDownloadedSize)
                {
                    await UpdateDownloadProgressAsync(targetDirectory, state.DownloadingFileName, effectiveDownloadedSize, cancellationToken);
                }
            }
        }
    }

    private static bool HasValidModelFiles(string directory)
    {
        var modelExtensions = new[] { ".gguf", ".safetensors", ".bin" };

        foreach (var ext in modelExtensions)
        {
            var files = Directory.GetFiles(directory, $"*{ext}", SearchOption.TopDirectoryOnly);
            if (files.Length > 0 && files.Any(f => new FileInfo(f).Length > 1024 * 1024)) // At least 1MB
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsFileIntegrityValid(string filePath)
    {
        try
        {
            // Basic integrity check - file can be opened and has content
            using var fs = File.OpenRead(filePath);
            return fs.Length > 0 && fs.CanRead;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Download state information with enhanced tracking
/// </summary>
internal class DownloadState
{
    public string ModelId { get; set; } = string.Empty;
    public string DownloadingFileName { get; set; } = string.Empty;
    public string ArtifactName { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public string TargetDirectory { get; set; } = string.Empty;
    public long TotalSize { get; set; }
    public long DownloadedBytes { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime LastUpdated { get; set; }
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }

    /// <summary>
    /// Gets the download progress as a percentage (0.0-1.0) with proper bounds checking
    /// </summary>
    public double Progress
    {
        get
        {
            if (TotalSize <= 0) return 0.0;
            var progress = (double)DownloadedBytes / TotalSize;
            return Math.Max(0.0, Math.Min(1.0, progress));
        }
    }

    /// <summary>
    /// Gets whether this download is complete
    /// </summary>
    public bool IsComplete => TotalSize > 0 && DownloadedBytes >= TotalSize;
}