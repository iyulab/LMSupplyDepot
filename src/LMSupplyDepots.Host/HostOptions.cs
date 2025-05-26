//namespace LMSupplyDepots.Host;

///// <summary>
///// Options for configuring the Host service
///// </summary>
//public class HostOptions
//{
//    /// <summary>
//    /// The directory where models are stored
//    /// </summary>
//    public string DataPath { get; set; } = Path.Combine(
//        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
//        "LMSupplyDepots");

//    /// <summary>
//    /// The maximum number of concurrent downloads
//    /// </summary>
//    public int MaxConcurrentDownloads { get; set; } = 2;

//    /// <summary>
//    /// Whether to verify model checksums after download
//    /// </summary>
//    public bool VerifyChecksums { get; set; } = true;

//    /// <summary>
//    /// The minimum free disk space required to download models (in bytes)
//    /// </summary>
//    public long MinimumFreeDiskSpace { get; set; } = 1024L * 1024 * 1024 * 10; // 10 GB

//    /// <summary>
//    /// The HuggingFace API token for accessing gated models
//    /// </summary>
//    public string? HuggingFaceApiToken { get; set; }

//    /// <summary>
//    /// Default timeout for inference operations in milliseconds
//    /// </summary>
//    public int DefaultTimeoutMs { get; set; } = 30000;

//    /// <summary>
//    /// Maximum number of concurrent inference operations
//    /// </summary>
//    public int MaxConcurrentOperations { get; set; } = 4;

//    /// <summary>
//    /// Whether to enable performance metrics collection
//    /// </summary>
//    public bool EnableMetrics { get; set; } = true;

//    /// <summary>
//    /// Whether to cache loaded models in memory
//    /// </summary>
//    public bool EnableModelCaching { get; set; } = true;

//    /// <summary>
//    /// Maximum number of models to keep in memory cache
//    /// </summary>
//    public int MaxCachedModels { get; set; } = 2;

//    /// <summary>
//    /// Directory for temporary files used during inference
//    /// </summary>
//    public string? TempDirectory { get; set; }

//    /// <summary>
//    /// LLama engine specific options
//    /// </summary>
//    public LLamaHostOptions? LLamaOptions { get; set; } = new();

//    /// <summary>
//    /// Whether to force CPU-only mode (disables all GPU acceleration)
//    /// </summary>
//    public bool ForceCpuOnly { get; set; } = false;
//}

///// <summary>
///// LLama engine specific configuration options for host
///// </summary>
//public class LLamaHostOptions
//{
//    /// <summary>
//    /// Number of threads to use for inference
//    /// </summary>
//    public int? Threads { get; set; }

//    /// <summary>
//    /// Number of GPU layers to offload
//    /// </summary>
//    public int? GpuLayers { get; set; }

//    /// <summary>
//    /// Context size for the model
//    /// </summary>
//    public int? ContextSize { get; set; }

//    /// <summary>
//    /// Batch size for processing
//    /// </summary>
//    public int? BatchSize { get; set; }

//    /// <summary>
//    /// Anti-prompt strings that stop generation
//    /// </summary>
//    public List<string>? AntiPrompt { get; set; }

//    /// <summary>
//    /// Whether to use memory mapping for loading models
//    /// </summary>
//    public bool UseMemoryMapping { get; set; } = true;

//    /// <summary>
//    /// Seed for random number generation (null for random)
//    /// </summary>
//    public int? Seed { get; set; }
//}