namespace LMSupplyDepots.ModelHub;

/// <summary>
/// Options for configuring the model hub.
/// </summary>
public class ModelHubOptions
{
    /// <summary>
    /// Gets or sets the base directory for storing models.
    /// If ModelsDirectory is not set, models will be stored in {DataPath}/models.
    /// </summary>
    public string DataPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LMSupplyDepots");

    /// <summary>
    /// Gets or sets the directory where models are actually stored.
    /// When set, this path is used directly without appending "models" subdirectory.
    /// When null, defaults to {DataPath}/models for backward compatibility.
    /// </summary>
    public string? ModelsDirectory { get; set; }

    /// <summary>
    /// Gets the effective models directory path, resolving ModelsDirectory or DataPath.
    /// </summary>
    public string GetModelsDirectory() =>
        ModelsDirectory ?? Path.Combine(DataPath, "models");

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