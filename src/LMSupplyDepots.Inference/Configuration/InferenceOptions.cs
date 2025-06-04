namespace LMSupplyDepots.Inference.Configuration;

/// <summary>
/// Global configuration options for the inference system
/// </summary>
public class InferenceOptions
{
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
    /// Engine-specific options for different model types
    /// </summary>
    public Dictionary<string, EngineOptions> EngineOptions { get; set; } = new();

    /// <summary>
    /// Get engine options for a specific engine type
    /// </summary>
    public EngineOptions GetEngineOptions(string engineType)
    {
        if (EngineOptions.TryGetValue(engineType, out var options))
        {
            return options;
        }

        // Return default options if not found
        return new EngineOptions();
    }
}