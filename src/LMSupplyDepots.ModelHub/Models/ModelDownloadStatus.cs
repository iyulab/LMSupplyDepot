namespace LMSupplyDepots.ModelHub.Models;

/// <summary>
/// Represents the status of a model download operation.
/// </summary>
public enum ModelDownloadStatus
{
    /// <summary>
    /// Download is being initialized.
    /// </summary>
    Initializing,

    /// <summary>
    /// Download is in progress.
    /// </summary>
    Downloading,

    /// <summary>
    /// Download is paused.
    /// </summary>
    Paused,

    /// <summary>
    /// Download is processing the downloaded files.
    /// </summary>
    Processing,

    /// <summary>
    /// Download has completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// Download has failed.
    /// </summary>
    Failed,

    /// <summary>
    /// Download was cancelled by the user.
    /// </summary>
    Cancelled
}