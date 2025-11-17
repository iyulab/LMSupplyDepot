namespace LMSupplyDepots.ModelHub;

/// <summary>
/// Options for configuring the model hub.
/// </summary>
public class ModelHubOptions
{
    /// <summary>
    /// Gets or sets the directory where models are stored.
    /// Models are stored in subdirectories organized by collection.
    /// Example: {ModelsDirectory}/publisher_model-name/model.gguf
    /// Default: %LocalAppData%/LMSupplyDepots/models
    /// </summary>
    public string ModelsDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LMSupplyDepots",
        "models");

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