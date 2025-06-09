namespace LMSupplyDepots.ModelHub.Models;

/// <summary>
/// Represents the progress of a model download operation with detailed status information
/// </summary>
public class ModelDownloadProgress
{
    /// <summary>
    /// Gets or sets the model being downloaded
    /// </summary>
    public required string ModelId { get; init; }

    /// <summary>
    /// Gets or sets the file being downloaded
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// Gets or sets the bytes downloaded so far
    /// </summary>
    public required long BytesDownloaded { get; init; }

    /// <summary>
    /// Gets or sets the total bytes to download
    /// </summary>
    public required long? TotalBytes { get; init; }

    /// <summary>
    /// Gets or sets the download speed in bytes per second
    /// </summary>
    public required double BytesPerSecond { get; init; }

    /// <summary>
    /// Gets or sets the estimated time remaining
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining { get; init; }

    /// <summary>
    /// Gets or sets the status of the download
    /// </summary>
    public ModelDownloadStatus Status { get; init; } = ModelDownloadStatus.Downloading;

    /// <summary>
    /// Gets or sets any error message if download failed
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets or sets when the download was started
    /// </summary>
    public DateTime? StartedAt { get; init; }

    /// <summary>
    /// Gets the progress percentage (0-100)
    /// </summary>
    public double ProgressPercentage
    {
        get
        {
            if (!TotalBytes.HasValue || TotalBytes.Value <= 0)
                return 0;

            var percentage = Math.Min(100.0, (BytesDownloaded * 100.0) / TotalBytes.Value);
            return Math.Max(0.0, percentage);
        }
    }

    /// <summary>
    /// Creates a new progress instance with updated status
    /// </summary>
    public ModelDownloadProgress WithStatus(ModelDownloadStatus newStatus)
    {
        return new ModelDownloadProgress
        {
            ModelId = ModelId,
            FileName = FileName,
            BytesDownloaded = BytesDownloaded,
            TotalBytes = TotalBytes,
            BytesPerSecond = BytesPerSecond,
            EstimatedTimeRemaining = EstimatedTimeRemaining,
            Status = newStatus,
            ErrorMessage = ErrorMessage,
            StartedAt = StartedAt
        };
    }

    /// <summary>
    /// Creates a new progress instance with updated bytes downloaded
    /// </summary>
    public ModelDownloadProgress WithBytesDownloaded(long newBytesDownloaded)
    {
        return new ModelDownloadProgress
        {
            ModelId = ModelId,
            FileName = FileName,
            BytesDownloaded = newBytesDownloaded,
            TotalBytes = TotalBytes,
            BytesPerSecond = BytesPerSecond,
            EstimatedTimeRemaining = EstimatedTimeRemaining,
            Status = Status,
            ErrorMessage = ErrorMessage,
            StartedAt = StartedAt
        };
    }

    /// <summary>
    /// Gets a formatted string representation of the download progress
    /// </summary>
    public string FormatProgress()
    {
        var percentStr = TotalBytes.HasValue
            ? $"{ProgressPercentage:F1}%"
            : "unknown %";

        var sizeStr = TotalBytes.HasValue
            ? $"{FormatSize(BytesDownloaded)} / {FormatSize(TotalBytes.Value)}"
            : $"{FormatSize(BytesDownloaded)} / unknown";

        var speedStr = $"{FormatSize((long)BytesPerSecond)}/s";

        var remainingStr = EstimatedTimeRemaining.HasValue
            ? FormatTimeRemaining(EstimatedTimeRemaining.Value)
            : "unknown time";

        return $"{percentStr} ({sizeStr}) at {speedStr}, {remainingStr} remaining";
    }

    /// <summary>
    /// Creates a progress instance for a completed download
    /// </summary>
    public static ModelDownloadProgress CreateCompleted(string modelId, string fileName, long totalBytes, DateTime? startedAt = null)
    {
        return new ModelDownloadProgress
        {
            ModelId = modelId,
            FileName = fileName,
            BytesDownloaded = totalBytes,
            TotalBytes = totalBytes,
            BytesPerSecond = 0,
            EstimatedTimeRemaining = TimeSpan.Zero,
            Status = ModelDownloadStatus.Completed,
            StartedAt = startedAt
        };
    }

    /// <summary>
    /// Creates a progress instance for a failed download
    /// </summary>
    public static ModelDownloadProgress CreateFailed(string modelId, string fileName, long bytesDownloaded, long? totalBytes, string errorMessage, DateTime? startedAt = null)
    {
        return new ModelDownloadProgress
        {
            ModelId = modelId,
            FileName = fileName,
            BytesDownloaded = bytesDownloaded,
            TotalBytes = totalBytes,
            BytesPerSecond = 0,
            EstimatedTimeRemaining = null,
            Status = ModelDownloadStatus.Failed,
            ErrorMessage = errorMessage,
            StartedAt = startedAt
        };
    }

    /// <summary>
    /// Creates a progress instance for a paused download
    /// </summary>
    public static ModelDownloadProgress CreatePaused(string modelId, string fileName, long bytesDownloaded, long? totalBytes, DateTime? startedAt = null)
    {
        return new ModelDownloadProgress
        {
            ModelId = modelId,
            FileName = fileName,
            BytesDownloaded = bytesDownloaded,
            TotalBytes = totalBytes,
            BytesPerSecond = 0,
            EstimatedTimeRemaining = null,
            Status = ModelDownloadStatus.Paused,
            StartedAt = startedAt
        };
    }

    /// <summary>
    /// Creates a progress instance for a downloading state
    /// </summary>
    public static ModelDownloadProgress CreateDownloading(string modelId, string fileName, long bytesDownloaded, long? totalBytes, double bytesPerSecond, TimeSpan? estimatedTimeRemaining = null, DateTime? startedAt = null)
    {
        return new ModelDownloadProgress
        {
            ModelId = modelId,
            FileName = fileName,
            BytesDownloaded = bytesDownloaded,
            TotalBytes = totalBytes,
            BytesPerSecond = bytesPerSecond,
            EstimatedTimeRemaining = estimatedTimeRemaining,
            Status = ModelDownloadStatus.Downloading,
            StartedAt = startedAt
        };
    }

    /// <summary>
    /// Creates a progress instance for not found state
    /// </summary>
    public static ModelDownloadProgress CreateNotFound(string modelId)
    {
        return new ModelDownloadProgress
        {
            ModelId = modelId,
            FileName = "",
            BytesDownloaded = 0,
            TotalBytes = null,
            BytesPerSecond = 0,
            Status = ModelDownloadStatus.Failed,
            ErrorMessage = "Model not found in downloads"
        };
    }

    private static string FormatSize(long? bytes)
    {
        if (!bytes.HasValue || bytes.Value < 0)
            return "unknown";

        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes.Value;
        int unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size:F2} {units[unitIndex]}";
    }

    private static string FormatTimeRemaining(TimeSpan timeSpan)
    {
        if (timeSpan.TotalDays >= 1)
            return $"{timeSpan.TotalDays:F1} days";
        else if (timeSpan.TotalHours >= 1)
            return $"{timeSpan.TotalHours:F1} hours";
        else if (timeSpan.TotalMinutes >= 1)
            return $"{timeSpan.TotalMinutes:F1} minutes";
        else
            return $"{timeSpan.TotalSeconds:F0} seconds";
    }
}