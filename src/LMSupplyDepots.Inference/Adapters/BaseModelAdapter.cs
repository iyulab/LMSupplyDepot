namespace LMSupplyDepots.Inference.Adapters;

/// <summary>
/// Base class for model adapters that connect to external model engines
/// </summary>
public abstract class BaseModelAdapter : IDisposable
{
    protected readonly ILogger _logger;
    protected bool _disposed;

    /// <summary>
    /// Name of the adapter
    /// </summary>
    public abstract string AdapterName { get; }

    /// <summary>
    /// The model format(s) this adapter supports
    /// </summary>
    public abstract IReadOnlyList<string> SupportedFormats { get; }

    /// <summary>
    /// Model types supported by this adapter
    /// </summary>
    public abstract IReadOnlyList<ModelType> SupportedModelTypes { get; }

    /// <summary>
    /// Initializes a new instance of a model adapter
    /// </summary>
    protected BaseModelAdapter(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Checks if the adapter can handle the specified model
    /// </summary>
    public virtual bool CanHandle(LMModel model)
    {
        if (model == null)
        {
            return false;
        }

        return SupportedFormats.Contains(model.Format, StringComparer.OrdinalIgnoreCase) &&
               SupportedModelTypes.Contains(model.Type);
    }

    /// <summary>
    /// Loads a model into memory
    /// </summary>
    public abstract Task<bool> LoadModelAsync(
        LMModel model,
        Dictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Unloads a model from memory
    /// </summary>
    public abstract Task<bool> UnloadModelAsync(
        string modelId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a model is currently loaded
    /// </summary>
    public abstract Task<bool> IsModelLoadedAsync(
        string modelId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a text generation engine for a loaded model
    /// </summary>
    public abstract Task<ITextGenerationEngine?> CreateTextGenerationEngineAsync(
        string modelId,
        Dictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an embedding engine for a loaded model
    /// </summary>
    public abstract Task<IEmbeddingEngine?> CreateEmbeddingEngineAsync(
        string modelId,
        Dictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes resources
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Protected implementation of Dispose pattern
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }

    /// <summary>
    /// Throws if the adapter is disposed
    /// </summary>
    protected void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, GetType().FullName);
    }
}