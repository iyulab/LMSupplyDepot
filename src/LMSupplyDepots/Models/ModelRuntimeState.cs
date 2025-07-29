namespace LMSupplyDepots.Models;

/// <summary>
/// Represents the runtime status of a model in the system
/// </summary>
public enum ModelStatus
{
    /// <summary>
    /// Model is not loaded in memory
    /// </summary>
    Unloaded = 0,

    /// <summary>
    /// Model is currently being loaded into memory
    /// </summary>
    Loading = 1,

    /// <summary>
    /// Model is loaded and ready for inference
    /// </summary>
    Loaded = 2,

    /// <summary>
    /// Model failed to load
    /// </summary>
    Failed = 3,

    /// <summary>
    /// Model is currently being unloaded from memory
    /// </summary>
    Unloading = 4
}

/// <summary>
/// Represents the runtime state of a model including status and metadata
/// </summary>
public class ModelRuntimeState
{
    /// <summary>
    /// The model ID
    /// </summary>
    public string ModelId { get; init; } = string.Empty;

    /// <summary>
    /// Current status of the model
    /// </summary>
    public ModelStatus Status { get; set; } = ModelStatus.Unloaded;

    /// <summary>
    /// Timestamp when the model was loaded into memory
    /// </summary>
    public DateTime? LoadedAt { get; set; }

    /// <summary>
    /// Timestamp when the status last changed
    /// </summary>
    public DateTime? LastStatusChange { get; set; }

    /// <summary>
    /// Error message if the model failed to load
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Adapter that loaded this model
    /// </summary>
    public string? AdapterName { get; set; }

    /// <summary>
    /// Additional metadata about the loaded model
    /// </summary>
    public Dictionary<string, object?> Metadata { get; set; } = new();

    /// <summary>
    /// Determines if the model is available for inference
    /// </summary>
    public bool IsAvailable => Status == ModelStatus.Loaded;

    /// <summary>
    /// Sets the model status to loaded with current timestamp
    /// </summary>
    public void SetLoaded(string? adapterName = null)
    {
        Status = ModelStatus.Loaded;
        LoadedAt = DateTime.UtcNow;
        LastStatusChange = DateTime.UtcNow;
        AdapterName = adapterName;
        ErrorMessage = null;
    }

    /// <summary>
    /// Sets the model status to unloaded and clears timestamps
    /// </summary>
    public void SetUnloaded()
    {
        Status = ModelStatus.Unloaded;
        LoadedAt = null;
        LastStatusChange = DateTime.UtcNow;
        ErrorMessage = null;
        AdapterName = null;
    }

    /// <summary>
    /// Sets the model status to loading
    /// </summary>
    public void SetLoading()
    {
        Status = ModelStatus.Loading;
        LastStatusChange = DateTime.UtcNow;
        ErrorMessage = null;
    }

    /// <summary>
    /// Sets the model status to failed with error message
    /// </summary>
    public void SetFailed(string errorMessage)
    {
        Status = ModelStatus.Failed;
        LastStatusChange = DateTime.UtcNow;
        ErrorMessage = errorMessage;
        LoadedAt = null;
        AdapterName = null;
    }

    /// <summary>
    /// Sets the model status to unloading
    /// </summary>
    public void SetUnloading()
    {
        Status = ModelStatus.Unloading;
        LastStatusChange = DateTime.UtcNow;
    }
}