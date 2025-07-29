using System.Collections.Concurrent;
using LMSupplyDepots.Models;
using Microsoft.Extensions.Logging;

namespace LMSupplyDepots.Inference.Services;

/// <summary>
/// Service for tracking model runtime state and usage statistics
/// </summary>
public class ModelStateService
{
    private readonly ILogger<ModelStateService> _logger;
    private readonly ConcurrentDictionary<string, ModelRuntimeState> _modelStates = new();
    private readonly ConcurrentDictionary<string, ModelUsageStatistics> _usageStats = new();

    /// <summary>
    /// Event fired when a model's status changes
    /// </summary>
    public event EventHandler<ModelStatusChangedEventArgs>? ModelStatusChanged;

    /// <summary>
    /// Initializes a new instance of the ModelStateService
    /// </summary>
    public ModelStateService(ILogger<ModelStateService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets the runtime state of a model
    /// </summary>
    public ModelRuntimeState GetModelState(string modelId)
    {
        return _modelStates.GetOrAdd(modelId, id => new ModelRuntimeState { ModelId = id });
    }

    /// <summary>
    /// Updates the status of a model
    /// </summary>
    public void UpdateModelStatus(string modelId, ModelStatus status, string? errorMessage = null, string? adapterName = null)
    {
        var state = GetModelState(modelId);
        var previousStatus = state.Status;

        switch (status)
        {
            case ModelStatus.Loading:
                state.SetLoading();
                break;
            case ModelStatus.Loaded:
                state.SetLoaded(adapterName);
                break;
            case ModelStatus.Failed:
                state.SetFailed(errorMessage ?? "Unknown error");
                break;
            case ModelStatus.Unloading:
                state.SetUnloading();
                break;
            case ModelStatus.Unloaded:
                state.SetUnloaded();
                break;
        }

        _logger.LogInformation("Model {ModelId} status changed from {PreviousStatus} to {NewStatus}",
            modelId, previousStatus, status);

        // Fire the status change event
        ModelStatusChanged?.Invoke(this, new ModelStatusChangedEventArgs
        {
            ModelId = modelId,
            PreviousStatus = previousStatus,
            NewStatus = status,
            Timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Checks if a model is loaded
    /// </summary>
    public bool IsModelLoaded(string modelId)
    {
        return _modelStates.TryGetValue(modelId, out var state) && state.IsAvailable;
    }

    /// <summary>
    /// Gets all loaded models
    /// </summary>
    public IReadOnlyList<ModelRuntimeState> GetLoadedModels()
    {
        return _modelStates.Values
            .Where(state => state.Status == ModelStatus.Loaded)
            .ToList();
    }

    /// <summary>
    /// Gets all model states
    /// </summary>
    public IReadOnlyDictionary<string, ModelRuntimeState> GetAllModelStates()
    {
        return _modelStates;
    }

    /// <summary>
    /// Records a model usage event
    /// </summary>
    public void RecordModelUsage(string modelId, ModelUsageType usageType, TimeSpan duration, int? tokenCount = null)
    {
        var stats = _usageStats.GetOrAdd(modelId, _ => new ModelUsageStatistics(modelId));
        stats.RecordUsage(usageType, duration, tokenCount);

        _logger.LogDebug("Recorded {UsageType} usage for model {ModelId}: {Duration}ms, {TokenCount} tokens",
            usageType, modelId, duration.TotalMilliseconds, tokenCount);
    }

    /// <summary>
    /// Gets usage statistics for a model
    /// </summary>
    public ModelUsageStatistics? GetModelUsageStatistics(string modelId)
    {
        return _usageStats.TryGetValue(modelId, out var stats) ? stats : null;
    }

    /// <summary>
    /// Gets usage statistics for all models
    /// </summary>
    public IReadOnlyDictionary<string, ModelUsageStatistics> GetAllUsageStatistics()
    {
        return _usageStats;
    }

    /// <summary>
    /// Clears usage statistics for a model
    /// </summary>
    public void ClearModelUsageStats(string modelId)
    {
        if (_usageStats.TryRemove(modelId, out _))
        {
            _logger.LogInformation("Cleared usage statistics for model {ModelId}", modelId);
        }
    }

    /// <summary>
    /// Removes a model from state tracking (when model is removed from system)
    /// </summary>
    public void RemoveModel(string modelId)
    {
        _modelStates.TryRemove(modelId, out _);
        _usageStats.TryRemove(modelId, out _);
        _logger.LogInformation("Removed model {ModelId} from state tracking", modelId);
    }
}

/// <summary>
/// Event arguments for model status changes
/// </summary>
public class ModelStatusChangedEventArgs : EventArgs
{
    /// <summary>
    /// The model ID
    /// </summary>
    public string ModelId { get; init; } = string.Empty;

    /// <summary>
    /// Previous status
    /// </summary>
    public ModelStatus PreviousStatus { get; init; }

    /// <summary>
    /// New status
    /// </summary>
    public ModelStatus NewStatus { get; init; }

    /// <summary>
    /// Timestamp of the change
    /// </summary>
    public DateTime Timestamp { get; init; }
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
/// Represents usage statistics for a model
/// </summary>
public class ModelUsageStatistics
{
    private readonly object _lock = new();

    /// <summary>
    /// The model ID
    /// </summary>
    public string ModelId { get; }

    /// <summary>
    /// The time the model was first used
    /// </summary>
    public DateTime? FirstUsedTime { get; private set; }

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
    /// Initializes a new instance of ModelUsageStatistics
    /// </summary>
    public ModelUsageStatistics(string modelId)
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
            var now = DateTime.UtcNow;
            FirstUsedTime ??= now;
            LastUsedTime = now;

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
    public void Clear()
    {
        lock (_lock)
        {
            FirstUsedTime = null;
            LastUsedTime = null;
            TextGenerationCount = 0;
            EmbeddingCount = 0;
            TotalTokensGenerated = 0;
            TotalTextGenerationTime = TimeSpan.Zero;
            TotalEmbeddingTime = TimeSpan.Zero;
        }
    }
}