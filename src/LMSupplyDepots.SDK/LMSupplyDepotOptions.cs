namespace LMSupplyDepots.SDK;

/// <summary>
/// Options for configuring the LMSupplyDepot
/// </summary>
public class LMSupplyDepotOptions
{
    /// <summary>
    /// The base directory for application data.
    /// If ModelsDirectory is not set, models will be stored in {DataPath}/models.
    /// </summary>
    public string DataPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LMSupplyDepots");

    /// <summary>
    /// The directory where models are actually stored.
    /// When set, this path is used directly without appending "models" subdirectory.
    /// When null, defaults to {DataPath}/models for backward compatibility.
    /// This can be configured via environment variable: LMSupplyDepots__ModelsDirectory
    /// </summary>
    public string? ModelsDirectory { get; set; }

    /// <summary>
    /// The maximum number of concurrent downloads
    /// </summary>
    public int MaxConcurrentDownloads { get; set; } = 2;

    /// <summary>
    /// The maximum number of concurrent file downloads
    /// </summary>
    public int MaxConcurrentFileDownloads { get; set; } = 4;

    /// <summary>
    /// Whether to verify model checksums after download
    /// </summary>
    public bool VerifyChecksums { get; set; } = true;

    /// <summary>
    /// The minimum free disk space required to download models (in bytes)
    /// </summary>
    public long MinimumFreeDiskSpace { get; set; } = 1024L * 1024 * 1024 * 10; // 10 GB

    /// <summary>
    /// The HuggingFace API token for accessing gated models
    /// </summary>
    public string? HuggingFaceApiToken { get; set; }

    /// <summary>
    /// The HTTP request timeout for API calls
    /// </summary>
    public TimeSpan HttpRequestTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The maximum number of retries for HTTP requests
    /// </summary>
    public int HttpMaxRetries { get; set; } = 3;

    /// <summary>
    /// Default timeout for inference operations in milliseconds
    /// </summary>
    public int DefaultTimeoutMs { get; set; } = 30000;

    /// <summary>
    /// Maximum number of concurrent inference operations
    /// </summary>
    public int MaxConcurrentOperations { get; set; } = 4;

    /// <summary>
    /// Whether to enable performance metrics collection
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// Whether to cache loaded models in memory
    /// </summary>
    public bool EnableModelCaching { get; set; } = true;

    /// <summary>
    /// Maximum number of models to keep in memory cache
    /// </summary>
    public int MaxCachedModels { get; set; } = 2;

    /// <summary>
    /// Directory for temporary files used during inference
    /// </summary>
    public string? TempDirectory { get; set; }

    /// <summary>
    /// Text generation specific options
    /// </summary>
    public TextGenerationOptions? TextGeneration { get; set; } = new();

    /// <summary>
    /// Embedding specific options
    /// </summary>
    public EmbeddingOptions? Embedding { get; set; } = new();

    /// <summary>
    /// LLama engine specific options
    /// </summary>
    public LLamaOptions? LLamaOptions { get; set; } = new();

    /// <summary>
    /// Whether to force CPU-only mode (disables all GPU acceleration)
    /// </summary>
    public bool ForceCpuOnly { get; set; } = false;
}

/// <summary>
/// Options for text generation
/// </summary>
public class TextGenerationOptions
{
    /// <summary>
    /// Default max tokens to generate
    /// </summary>
    public int DefaultMaxTokens { get; set; } = 256;

    /// <summary>
    /// Default temperature for text generation
    /// </summary>
    public float DefaultTemperature { get; set; } = 0.7f;

    /// <summary>
    /// Default top-p sampling value
    /// </summary>
    public float DefaultTopP { get; set; } = 0.95f;

    /// <summary>
    /// Default repetition penalty
    /// </summary>
    public float DefaultRepetitionPenalty { get; set; } = 1.1f;

    /// <summary>
    /// Converts options to parameters dictionary
    /// </summary>
    public Dictionary<string, object?> ToParameters()
    {
        return new Dictionary<string, object?>
        {
            ["max_tokens"] = DefaultMaxTokens,
            ["temperature"] = DefaultTemperature,
            ["top_p"] = DefaultTopP,
            ["repetition_penalty"] = DefaultRepetitionPenalty
        };
    }
}

/// <summary>
/// Options for embeddings
/// </summary>
public class EmbeddingOptions
{
    /// <summary>
    /// Whether to normalize embeddings by default
    /// </summary>
    public bool DefaultNormalize { get; set; } = true;

    /// <summary>
    /// Converts options to parameters dictionary
    /// </summary>
    public Dictionary<string, object?> ToParameters()
    {
        return new Dictionary<string, object?>
        {
            ["normalize"] = DefaultNormalize
        };
    }
}

/// <summary>
/// LLama engine specific options
/// </summary>
public class LLamaOptions
{
    /// <summary>
    /// Number of threads to use for inference
    /// </summary>
    public int? Threads { get; set; }

    /// <summary>
    /// Number of GPU layers to offload
    /// </summary>
    public int? GpuLayers { get; set; }

    /// <summary>
    /// Context size for the model
    /// </summary>
    public int? ContextSize { get; set; }

    /// <summary>
    /// Batch size for processing
    /// </summary>
    public int? BatchSize { get; set; }

    /// <summary>
    /// Anti-prompt strings that stop generation
    /// </summary>
    public List<string> AntiPrompt { get; set; } = new List<string>();

    /// <summary>
    /// Whether to use memory mapping for loading models
    /// </summary>
    public bool UseMemoryMapping { get; set; } = true;

    /// <summary>
    /// Seed for random number generation (null for random)
    /// </summary>
    public int? Seed { get; set; }

    /// <summary>
    /// Converts options to parameters dictionary
    /// </summary>
    public Dictionary<string, object?> ToParameters()
    {
        var parameters = new Dictionary<string, object?>();

        if (Threads.HasValue)
        {
            parameters["threads"] = Threads.Value;
        }

        if (GpuLayers.HasValue)
        {
            parameters["gpu_layers"] = GpuLayers.Value;
        }

        if (ContextSize.HasValue)
        {
            parameters["context_size"] = ContextSize.Value;
        }

        if (BatchSize.HasValue)
        {
            parameters["batch_size"] = BatchSize.Value;
        }

        if (AntiPrompt.Count > 0)
        {
            parameters["antiprompt"] = AntiPrompt;
        }

        parameters["use_mmap"] = UseMemoryMapping;

        if (Seed.HasValue)
        {
            parameters["seed"] = Seed.Value;
        }

        return parameters;
    }
}