using LMSupplyDepots.Inference.Adapters;

namespace LMSupplyDepots.Inference.Services;

/// <summary>
/// Model loader service that manages models through repository and maintains load state
/// </summary>
public class RepositoryModelLoaderService : IModelLoader, IDisposable
{
    private readonly IModelRepository _repository;
    private readonly ILogger<RepositoryModelLoaderService> _logger;
    private readonly IEnumerable<BaseModelAdapter> _adapters;
    private readonly ConcurrentDictionary<string, LMModel> _loadedModels = new();
    private bool _disposed;

    public RepositoryModelLoaderService(
        IModelRepository repository,
        ILogger<RepositoryModelLoaderService> logger,
        IEnumerable<BaseModelAdapter> adapters)
    {
        _repository = repository;
        _logger = logger;
        _adapters = adapters;

        // Log available adapters for debugging
        var adapterList = adapters.ToList();
        _logger.LogInformation("RepositoryModelLoaderService initialized with {AdapterCount} adapters: {AdapterNames}",
            adapterList.Count,
            string.Join(", ", adapterList.Select(a => a.AdapterName)));
    }

    /// <summary>
    /// Loads a model into memory and updates its load state
    /// </summary>
    public async Task<LMModel> LoadModelAsync(
        string modelId,
        Dictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if already loaded
            if (_loadedModels.TryGetValue(modelId, out var existingModel))
            {
                _logger.LogInformation("Model {ModelId} is already loaded", modelId);
                return existingModel;
            }

            // Get model from repository
            var model = await _repository.GetModelAsync(modelId, cancellationToken);
            if (model == null)
            {
                throw new ModelLoadException(modelId, "Model not found in repository");
            }

            // Validate model can be loaded
            ValidateModelForLoading(model);

            // Find a suitable adapter for the model
            _logger.LogInformation("Finding adapter for model {ModelId} with format '{Format}' and type '{Type}'",
                modelId, model.Format, model.Type);

            var adapter = FindAdapter(model);
            if (adapter == null)
            {
                _logger.LogError("No adapter found for model {ModelId} with format '{Format}' and type '{Type}'. Available adapters: {AdapterNames}",
                    modelId, model.Format, model.Type,
                    string.Join(", ", _adapters.Select(a => $"{a.AdapterName}({string.Join(",", a.SupportedFormats)})")));

                throw new ModelLoadException(modelId,
                    $"No adapter found for model with format '{model.Format}' and type '{model.Type}'");
            }

            // Load the model using the adapter
            _logger.LogInformation("Loading model {ModelId} using adapter {AdapterName}",
                modelId, adapter.AdapterName);

            var success = await adapter.LoadModelAsync(model, parameters, cancellationToken);

            _logger.LogInformation("Adapter {AdapterName} returned {Success} for model {ModelId}",
                adapter.AdapterName, success, modelId);

            if (!success)
            {
                throw new ModelLoadException(modelId, "Failed to load model using adapter");
            }

            // Mark as loaded and update timestamps
            model.SetLoaded();

            // Add to loaded models cache
            _loadedModels[modelId] = model;

            // Update repository with new load state
            await _repository.SaveModelAsync(model, cancellationToken);

            _logger.LogInformation("Model {ModelId} loaded successfully at {LoadedAt}",
                modelId, model.LoadedAt);

            return model;
        }
        catch (Exception ex) when (!(ex is ModelLoadException))
        {
            _logger.LogError(ex, "Failed to load model {ModelId}", modelId);
            throw new ModelLoadException(modelId, "Error loading model", ex);
        }
    }

    /// <summary>
    /// Unloads a model from memory and updates its load state
    /// </summary>
    public async Task UnloadModelAsync(
        string modelId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Remove from loaded models cache
            if (_loadedModels.TryRemove(modelId, out var model))
            {
                // Find the adapter for this model
                var adapter = FindAdapter(model);
                if (adapter != null)
                {
                    // Unload from the adapter first
                    _logger.LogInformation("Unloading model {ModelId} using adapter {AdapterName}",
                        modelId, adapter.AdapterName);

                    var success = await adapter.UnloadModelAsync(modelId, cancellationToken);
                    if (!success)
                    {
                        _logger.LogWarning("Adapter failed to unload model {ModelId}", modelId);
                    }
                }

                // Mark as unloaded
                model.SetUnloaded();

                // Update repository with new load state
                await _repository.SaveModelAsync(model, cancellationToken);

                _logger.LogInformation("Model {ModelId} unloaded successfully", modelId);
            }
            else
            {
                // Model wasn't in our cache, but check repository and update if needed
                var repoModel = await _repository.GetModelAsync(modelId, cancellationToken);
                if (repoModel != null && repoModel.IsLoaded)
                {
                    // Try to unload using adapter even if not in cache
                    var adapter = FindAdapter(repoModel);
                    if (adapter != null)
                    {
                        _logger.LogInformation("Unloading model {ModelId} using adapter {AdapterName} (not in cache)",
                            modelId, adapter.AdapterName);

                        await adapter.UnloadModelAsync(modelId, cancellationToken);
                    }

                    repoModel.SetUnloaded();
                    await _repository.SaveModelAsync(repoModel, cancellationToken);
                    _logger.LogInformation("Model {ModelId} load state updated to unloaded", modelId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unload model {ModelId}", modelId);
            throw;
        }
    }

    /// <summary>
    /// Checks if a model is currently loaded
    /// </summary>
    public async Task<bool> IsModelLoadedAsync(
        string modelId,
        CancellationToken cancellationToken = default)
    {
        // First check our in-memory cache
        if (_loadedModels.ContainsKey(modelId))
        {
            return true;
        }

        // Fallback to repository check
        var model = await _repository.GetModelAsync(modelId, cancellationToken);
        return model?.IsLoaded ?? false;
    }

    /// <summary>
    /// Gets a list of currently loaded models
    /// </summary>
    public async Task<IReadOnlyList<LMModel>> GetLoadedModelsAsync(
        CancellationToken cancellationToken = default)
    {
        var loadedModels = new List<LMModel>();

        // Get fresh data from repository for all cached models to ensure alias information is up-to-date
        foreach (var cachedModel in _loadedModels.Values)
        {
            var freshModel = await _repository.GetModelAsync(cachedModel.Id, cancellationToken);
            if (freshModel != null && freshModel.IsLoaded)
            {
                // Update cache with fresh model data (including updated alias)
                _loadedModels[cachedModel.Id] = freshModel;
                loadedModels.Add(freshModel);
            }
            else if (freshModel != null)
            {
                // Model exists but is marked as unloaded, remove from cache
                _loadedModels.TryRemove(cachedModel.Id, out _);
            }
        }

        // Also check repository for any models marked as loaded that might not be in our cache
        // This can happen if the service was restarted
        var allModels = await _repository.ListModelsAsync(
            null, null, 0, int.MaxValue, cancellationToken);

        foreach (var model in allModels)
        {
            if (model.IsLoaded && !_loadedModels.ContainsKey(model.Id))
            {
                // Model is marked as loaded in repository but not in our cache
                // This might indicate the service was restarted, so we should verify
                _logger.LogWarning("Model {ModelId} is marked as loaded in repository but not in memory cache. " +
                    "This might indicate a service restart. Marking as unloaded.", model.Id);

                // Mark as unloaded since we don't have it in memory
                model.SetUnloaded();
                await _repository.SaveModelAsync(model, cancellationToken);
            }
        }

        return loadedModels.AsReadOnly();
    }

    /// <summary>
    /// Finds a suitable adapter for the given model
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
    /// Initializes the service by synchronizing load states
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Initializing RepositoryModelLoaderService and synchronizing load states");

            // Get all models from repository
            var allModels = await _repository.ListModelsAsync(
                null, null, 0, int.MaxValue, cancellationToken);

            // Mark all models as unloaded since we're starting fresh
            foreach (var model in allModels.Where(m => m.IsLoaded))
            {
                model.SetUnloaded();
                await _repository.SaveModelAsync(model, cancellationToken);
            }

            _logger.LogInformation("Load state synchronization completed. All models marked as unloaded.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize RepositoryModelLoaderService");
            // Don't throw here as this is initialization - the service should still be usable
        }
    }

    /// <summary>
    /// Validates that a model can be loaded
    /// </summary>
    private void ValidateModelForLoading(LMModel model)
    {
        if (string.IsNullOrEmpty(model.LocalPath))
        {
            throw new ModelLoadException(model.Id, "Model path is not defined");
        }

        if (!Directory.Exists(model.LocalPath) && !File.Exists(model.LocalPath))
        {
            throw new ModelLoadException(model.Id, $"Model path does not exist: {model.LocalPath}");
        }

        // Additional validation can be added here
    }

    /// <summary>
    /// Updates the load state of a model in both cache and repository
    /// </summary>
    public async Task UpdateModelLoadStateAsync(string modelId, bool isLoaded, CancellationToken cancellationToken = default)
    {
        var model = await _repository.GetModelAsync(modelId, cancellationToken);
        if (model == null)
        {
            _logger.LogWarning("Attempted to update load state for non-existent model {ModelId}", modelId);
            return;
        }

        if (isLoaded)
        {
            model.SetLoaded();
            _loadedModels[modelId] = model;
        }
        else
        {
            model.SetUnloaded();
            _loadedModels.TryRemove(modelId, out _);
        }

        await _repository.SaveModelAsync(model, cancellationToken);

        _logger.LogDebug("Updated load state for model {ModelId}: {IsLoaded}", modelId, isLoaded);
    }

    /// <summary>
    /// Disposes resources and cleans up
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            // Mark all loaded models as unloaded
            try
            {
                var updateTasks = _loadedModels.Values.Select(async model =>
                {
                    try
                    {
                        model.SetUnloaded();
                        await _repository.SaveModelAsync(model, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to update load state for model {ModelId} during disposal", model.Id);
                    }
                });

                Task.WaitAll(updateTasks.ToArray(), TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during cleanup of loaded models");
            }

            _loadedModels.Clear();
            _disposed = true;
        }
    }
}