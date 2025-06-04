namespace LMSupplyDepots.ModelHub.Models;

/// <summary>
/// Information about a download operation
/// </summary>
public class DownloadInfo
{
    /// <summary>
    /// Model ID being downloaded
    /// </summary>
    public string ModelId { get; set; } = string.Empty;

    /// <summary>
    /// Current status of the download
    /// </summary>
    public ModelDownloadStatus Status { get; set; }

    /// <summary>
    /// Current progress information
    /// </summary>
    public ModelDownloadProgress? Progress { get; set; }

    /// <summary>
    /// When the download was started
    /// </summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>
    /// Model information if available
    /// </summary>
    public LMModel? ModelInfo { get; set; }
}