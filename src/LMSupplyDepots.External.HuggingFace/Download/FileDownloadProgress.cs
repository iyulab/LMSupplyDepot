using LMSupplyDepots.External.HuggingFace.Common;

namespace LMSupplyDepots.External.HuggingFace.Download;

/// <summary>
/// Represents immutable information about the progress of a file download.
/// </summary>
public record FileDownloadProgress
{
    /// <summary>
    /// Gets a value indicating whether the file download is completed.
    /// </summary>
    public bool IsCompleted { get; init; }

    /// <summary>
    /// Gets the path where the file is being downloaded to.
    /// </summary>
    public string UploadPath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the current number of bytes downloaded.
    /// </summary>
    public long CurrentBytes { get; init; }

    /// <summary>
    /// Gets the total number of bytes to be downloaded.
    /// </summary>
    public long? TotalBytes { get; init; }

    /// <summary>
    /// Gets the download speed in bytes per second.
    /// </summary>
    public double DownloadSpeed { get; init; }

    /// <summary>
    /// Gets the remaining time for the download.
    /// </summary>
    public TimeSpan? RemainingTime { get; init; }

    /// <summary>
    /// Gets the download progress from 0.0 to 1.0.
    /// </summary>
    public double? DownloadProgress { get; init; }

    /// <summary>
    /// Gets the formatted current size of the downloaded file.
    /// </summary>
    public string FormattedCurrentSize => StringFormatter.FormatSize(CurrentBytes);

    /// <summary>
    /// Gets the formatted total size of the file to be downloaded.
    /// </summary>
    public string FormattedTotalSize => StringFormatter.FormatSize(TotalBytes ?? 0);

    /// <summary>
    /// Gets the formatted download speed.
    /// </summary>
    public string FormattedDownloadSpeed => StringFormatter.FormatSpeed(DownloadSpeed);

    /// <summary>
    /// Gets the formatted remaining time.
    /// </summary>
    public string FormattedRemainingTime => StringFormatter.FormatTimeSpan(RemainingTime);

    /// <summary>
    /// Gets the formatted download progress as a percentage.
    /// </summary>
    public string FormattedProgress => StringFormatter.FormatProgress(DownloadProgress);

    /// <summary>
    /// Creates a new instance of FileDownloadProgress with the specified completion status.
    /// </summary>
    public static FileDownloadProgress CreateCompleted(string uploadPath, long totalBytes) =>
        new()
        {
            IsCompleted = true,
            UploadPath = uploadPath,
            CurrentBytes = totalBytes,
            TotalBytes = totalBytes,
            DownloadSpeed = 0,
            DownloadProgress = 1.0,
            RemainingTime = TimeSpan.Zero
        };

    /// <summary>
    /// Creates a new instance of FileDownloadProgress with updated progress information.
    /// </summary>
    public static FileDownloadProgress CreateProgress(
        string uploadPath,
        long currentBytes,
        long? totalBytes,
        double downloadSpeed,
        TimeSpan? remainingTime = null)
    {
        double? progress = totalBytes.HasValue
            ? (double)currentBytes / totalBytes.Value
            : null;

        return new FileDownloadProgress
        {
            IsCompleted = false,
            UploadPath = uploadPath,
            CurrentBytes = currentBytes,
            TotalBytes = totalBytes,
            DownloadSpeed = downloadSpeed,
            DownloadProgress = progress,
            RemainingTime = remainingTime
        };
    }

    public override string ToString() =>
        $"""
        Download Status: {(IsCompleted ? "Completed" : "In Progress")}
        Current Size: {FormattedCurrentSize}
        Total Size: {FormattedTotalSize}
        Download Speed: {FormattedDownloadSpeed}
        Progress: {FormattedProgress}
        Remaining Time: {FormattedRemainingTime}
        Upload Path: {UploadPath}
        """;
}