namespace LMSupplyDepots.External.HuggingFace.Download;

/// <summary>
/// Represents the state of a model file download
/// </summary>
public class ModelFileDownloadState
{
    /// <summary>
    /// Gets or sets the repository ID of the model
    /// </summary>
    public string ModelId { get; set; } = "";

    /// <summary>
    /// Gets or sets the file path within the repository
    /// </summary>
    public string FilePath { get; set; } = "";

    /// <summary>
    /// Gets or sets the total size of the file
    /// </summary>
    public long TotalSize { get; set; }

    /// <summary>
    /// Gets or sets the number of bytes downloaded
    /// </summary>
    public long DownloadedSize { get; set; }

    /// <summary>
    /// Gets or sets whether the download is completed
    /// </summary>
    public bool IsCompleted { get; set; }

    /// <summary>
    /// Gets or sets the last attempt timestamp
    /// </summary>
    public DateTime LastAttempt { get; set; }

    /// <summary>
    /// Gets the download progress as a percentage (0-1)
    /// </summary>
    public double Progress => TotalSize > 0 ? (double)DownloadedSize / TotalSize : 0;
}