namespace LMSupplyDepots.ModelHub;

/// <summary>
/// Options for configuring the model hub.
/// </summary>
public class ModelHubOptions
{
    /// <summary>
    /// Gets or sets the base directory for storing models.
    /// </summary>
    public string DataPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LMSupplyDepots");

    /// <summary>
    /// Gets or sets the maximum number of concurrent downloads.
    /// </summary>
    public int MaxConcurrentDownloads { get; set; } = 2;

    /// <summary>
    /// Gets or sets whether to verify model checksums after download.
    /// </summary>
    public bool VerifyChecksums { get; set; } = true;

    /// <summary>
    /// Gets or sets the minimum free disk space (in bytes) required to download models.
    /// </summary>
    public long MinimumFreeDiskSpace { get; set; } = 1024L * 1024 * 1024 * 10; // 10 GB
}