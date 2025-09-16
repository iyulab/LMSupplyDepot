using LMSupplyDepots.Inference.Adapters;
using LMSupplyDepots.Inference.Configuration;

namespace LMSupplyDepots.Inference.Services;

/// <summary>
/// Service for loading and unloading models
/// </summary>
public class ModelLoaderService : IModelLoader, IDisposable
{
    private readonly ILogger<ModelLoaderService> _logger;
    private readonly IOptionsMonitor<InferenceOptions> _options;
    private readonly IEnumerable<BaseModelAdapter> _adapters;
    private readonly ConcurrentDictionary<string, LMModel> _loadedModels = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the ModelLoaderService
    /// </summary>
    public ModelLoaderService(
        ILogger<ModelLoaderService> logger,
        IOptionsMonitor<InferenceOptions> options,
        IEnumerable<BaseModelAdapter> adapters)
    {
        _logger = logger;
        _options = options;
        _adapters = adapters;
    }

    /// <summary>
    /// Loads a model into memory
    /// </summary>
    public async Task<LMModel> LoadModelAsync(
        string modelId,
        Dictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // Check if already loaded
        if (_loadedModels.TryGetValue(modelId, out var loadedModel))
        {
            _logger.LogInformation("Model {ModelId} is already loaded", modelId);
            return loadedModel;
        }

        var model = await GetModelAsync(modelId, cancellationToken);
        if (model == null)
        {
            throw new ModelLoadException(modelId, "Model not found");
        }

        // Find a suitable adapter
        var adapter = FindAdapter(model);
        if (adapter == null)
        {
            throw new ModelLoadException(modelId,
                $"No adapter found for model with format '{model.Format}' and type '{model.Type}'");
        }

        // Load the model
        _logger.LogInformation("Loading model {ModelId} using adapter {AdapterName}",
            modelId, adapter.AdapterName);

        var success = await adapter.LoadModelAsync(model, parameters, cancellationToken);
        if (!success)
        {
            throw new ModelLoadException(modelId, "Failed to load model");
        }

        // Store in cache
        _loadedModels[modelId] = model;

        // Enforce model cache limit
        EnforceModelCacheLimit();

        return model;
    }

    /// <summary>
    /// Unloads a model from memory
    /// </summary>
    public async Task UnloadModelAsync(
        string modelId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!_loadedModels.TryGetValue(modelId, out var model))
        {
            _logger.LogInformation("Model {ModelId} is not loaded", modelId);
            return;
        }

        // Find the adapter
        var adapter = FindAdapter(model);
        if (adapter == null)
        {
            _logger.LogWarning("No adapter found for model {ModelId}, removing from loaded models", modelId);
            _loadedModels.TryRemove(modelId, out _);
            return;
        }

        // Unload the model
        _logger.LogInformation("Unloading model {ModelId} using adapter {AdapterName}",
            modelId, adapter.AdapterName);

        await adapter.UnloadModelAsync(modelId, cancellationToken);
        _loadedModels.TryRemove(modelId, out _);
    }

    /// <summary>
    /// Checks if a model is currently loaded
    /// </summary>
    public async Task<bool> IsModelLoadedAsync(
        string modelId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!_loadedModels.TryGetValue(modelId, out var model))
        {
            return false;
        }

        // Find the adapter and check if it's loaded
        var adapter = FindAdapter(model);
        if (adapter == null)
        {
            return false;
        }

        return await adapter.IsModelLoadedAsync(modelId, cancellationToken);
    }

    /// <summary>
    /// Gets a list of currently loaded models
    /// </summary>
    public Task<IReadOnlyList<LMModel>> GetLoadedModelsAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var models = _loadedModels.Values.ToList();
        return Task.FromResult<IReadOnlyList<LMModel>>(models);
    }

    /// <summary>
    /// Gets a model by its ID (override in derived classes)
    /// </summary>
    protected virtual Task<LMModel?> GetModelAsync(string modelId, CancellationToken cancellationToken)
    {
        // This should be implemented by derived classes that have access to a model repository
        throw new NotImplementedException("GetModelAsync must be implemented by derived classes");
    }

    /// <summary>
    /// Finds an adapter that can handle the model
    /// </summary>
    private BaseModelAdapter? FindAdapter(LMModel model)
    {
        foreach (var adapter in _adapters)
        {
            if (adapter.CanHandle(model))
            {
                return adapter;
            }
        }

        return null;
    }

    /// <summary>
    /// Enforces the model cache limit by unloading excess models
    /// </summary>
    private void EnforceModelCacheLimit()
    {
        if (!_options.CurrentValue.EnableModelCaching)
        {
            return;
        }

        var maxModels = _options.CurrentValue.MaxCachedModels;
        if (maxModels <= 0 || _loadedModels.Count <= maxModels)
        {
            return;
        }

        // Get models to unload (oldest ones first)
        var modelsToUnload = _loadedModels.Values
            .OrderBy(m => m.Id) // Simple ordering, could be improved
            .Take(_loadedModels.Count - maxModels)
            .ToList();

        foreach (var model in modelsToUnload)
        {
            _logger.LogInformation("Unloading model {ModelId} to enforce cache limit", model.Id);

            try
            {
                UnloadModelAsync(model.Id).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unloading model {ModelId}", model.Id);
            }
        }
    }

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
            if (disposing)
            {
                // Unload all models
                foreach (var modelId in _loadedModels.Keys)
                {
                    try
                    {
                        UnloadModelAsync(modelId).GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error unloading model {ModelId} during disposal", modelId);
                    }
                }

                _loadedModels.Clear();
            }

            _disposed = true;
        }
    }

    /// <summary>
    /// Throws if the service is disposed
    /// </summary>
    protected void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, GetType().FullName ?? GetType().Name);
    }
}