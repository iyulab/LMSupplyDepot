namespace LMSupplyDepots.Inference.Services;

/// <summary>
/// Service for tracking model state and usage statistics
/// </summary>
public class ModelStateService
{
    private readonly ILogger<ModelStateService> _logger;
    private readonly ConcurrentDictionary<string, ModelState> _modelStates = new();

    /// <summary>
    /// Initializes a new instance of the ModelStateService
    /// </summary>
    public ModelStateService(ILogger<ModelStateService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets the state of a model
    /// </summary>
    public ModelState GetModelState(string modelId)
    {
        return _modelStates.GetOrAdd(modelId, _ => new ModelState(modelId));
    }

    /// <summary>
    /// Records a model usage event
    /// </summary>
    public void RecordModelUsage(string modelId, ModelUsageType usageType, TimeSpan duration, int? tokenCount = null)
    {
        var state = GetModelState(modelId);
        state.RecordUsage(usageType, duration, tokenCount);

        _logger.LogDebug("Recorded {UsageType} usage for model {ModelId}: {Duration}ms, {TokenCount} tokens",
            usageType, modelId, duration.TotalMilliseconds, tokenCount);
    }

    /// <summary>
    /// Updates a model's loading state
    /// </summary>
    public void UpdateModelLoadingState(string modelId, bool isLoaded)
    {
        var state = GetModelState(modelId);
        state.IsLoaded = isLoaded;
        state.LastLoadStateChange = DateTime.UtcNow;

        _logger.LogInformation("Model {ModelId} is now {LoadState}",
            modelId, isLoaded ? "loaded" : "unloaded");
    }

    /// <summary>
    /// Gets all model states
    /// </summary>
    public IReadOnlyDictionary<string, ModelState> GetAllModelStates()
    {
        return _modelStates;
    }

    /// <summary>
    /// Clears usage statistics for a model
    /// </summary>
    public void ClearModelUsageStats(string modelId)
    {
        if (_modelStates.TryGetValue(modelId, out var state))
        {
            state.ClearUsageStats();
            _logger.LogInformation("Cleared usage statistics for model {ModelId}", modelId);
        }
    }
}

/// <summary>
/// The type of model usage
/// </summary>
public enum ModelUsageType
{
    TextGeneration,
    Embedding
}

/// <summary>
/// Represents the state and usage statistics of a model
/// </summary>
public class ModelState
{
    private readonly object _lock = new();

    /// <summary>
    /// The model ID
    /// </summary>
    public string ModelId { get; }

    /// <summary>
    /// Whether the model is currently loaded
    /// </summary>
    public bool IsLoaded { get; set; }

    /// <summary>
    /// The time the model was first loaded
    /// </summary>
    public DateTime? FirstLoadTime { get; private set; }

    /// <summary>
    /// The time the model's load state last changed
    /// </summary>
    public DateTime? LastLoadStateChange { get; set; }

    /// <summary>
    /// The time the model was last used
    /// </summary>
    public DateTime? LastUsedTime { get; private set; }

    /// <summary>
    /// Total number of text generation requests
    /// </summary>
    public int TextGenerationCount { get; private set; }

    /// <summary>
    /// Total number of embedding requests
    /// </summary>
    public int EmbeddingCount { get; private set; }

    /// <summary>
    /// Total number of tokens generated
    /// </summary>
    public long TotalTokensGenerated { get; private set; }

    /// <summary>
    /// Total time spent on text generation
    /// </summary>
    public TimeSpan TotalTextGenerationTime { get; private set; }

    /// <summary>
    /// Total time spent on embedding generation
    /// </summary>
    public TimeSpan TotalEmbeddingTime { get; private set; }

    /// <summary>
    /// Initializes a new instance of ModelState
    /// </summary>
    public ModelState(string modelId)
    {
        ModelId = modelId;
    }

    /// <summary>
    /// Records a model usage event
    /// </summary>
    public void RecordUsage(ModelUsageType usageType, TimeSpan duration, int? tokenCount = null)
    {
        lock (_lock)
        {
            LastUsedTime = DateTime.UtcNow;

            if (FirstLoadTime == null && IsLoaded)
            {
                FirstLoadTime = DateTime.UtcNow;
            }

            switch (usageType)
            {
                case ModelUsageType.TextGeneration:
                    TextGenerationCount++;
                    TotalTextGenerationTime += duration;
                    if (tokenCount.HasValue)
                    {
                        TotalTokensGenerated += tokenCount.Value;
                    }
                    break;

                case ModelUsageType.Embedding:
                    EmbeddingCount++;
                    TotalEmbeddingTime += duration;
                    break;
            }
        }
    }

    /// <summary>
    /// Clears usage statistics
    /// </summary>
    public void ClearUsageStats()
    {
        lock (_lock)
        {
            TextGenerationCount = 0;
            EmbeddingCount = 0;
            TotalTokensGenerated = 0;
            TotalTextGenerationTime = TimeSpan.Zero;
            TotalEmbeddingTime = TimeSpan.Zero;
        }
    }
}