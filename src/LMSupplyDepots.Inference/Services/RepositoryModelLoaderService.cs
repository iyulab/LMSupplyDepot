using LMSupplyDepots.Inference.Adapters;
using LMSupplyDepots.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace LMSupplyDepots.Inference.Services;

/// <summary>
/// Model loader service that manages models through repository without persisting load state
/// </summary>
public class RepositoryModelLoaderService : IModelLoader, IDisposable
{
    private readonly IModelRepository _repository;
    private readonly ILogger<RepositoryModelLoaderService> _logger;
    private readonly IEnumerable<BaseModelAdapter> _adapters;
    private readonly ModelStateService _modelStateService;
    private readonly ConcurrentDictionary<string, LMModel> _loadedModels = new();
    private bool _disposed;

    public RepositoryModelLoaderService(
        IModelRepository repository,
        ILogger<RepositoryModelLoaderService> logger,
        IEnumerable<BaseModelAdapter> adapters,
        ModelStateService modelStateService)
    {
        _repository = repository;
        _logger = logger;
        _adapters = adapters;
        _modelStateService = modelStateService;

        // Log available adapters for debugging
        var adapterList = adapters.ToList();
        _logger.LogInformation("RepositoryModelLoaderService initialized with {AdapterCount} adapters: {AdapterNames}",
            adapterList.Count,
            string.Join(", ", adapterList.Select(a => a.AdapterName)));
    }

    /// <summary>
    /// Loads a model into memory
    /// </summary>
    public async Task<LMModel> LoadModelAsync(
        string modelId,
        Dictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get model from repository first to resolve alias to actual ID
            var model = await _repository.GetModelAsync(modelId, cancellationToken);
            if (model == null)
            {
                throw new ModelLoadException(modelId, "Model not found in repository");
            }

            // Check if already loaded using the actual model ID
            if (_loadedModels.TryGetValue(model.Id, out var existingModel))
            {
                _logger.LogInformation("Model {ModelId} is already loaded", model.Id);
                return existingModel;
            }

            // Update status to loading
            _modelStateService.UpdateModelStatus(model.Id, ModelStatus.Loading);

            // Validate model can be loaded
            ValidateModelForLoading(model);

            // Find a suitable adapter for the model
            _logger.LogInformation("Finding adapter for model {ModelId} with format '{Format}' and type '{Type}'",
                model.Id, model.Format, model.Type);

            var adapter = FindAdapter(model);
            if (adapter == null)
            {
                var errorMessage = $"No adapter found for model with format '{model.Format}' and type '{model.Type}'";
                _modelStateService.UpdateModelStatus(model.Id, ModelStatus.Failed, errorMessage);

                _logger.LogError("No adapter found for model {ModelId} with format '{Format}' and type '{Type}'. Available adapters: {AdapterNames}",
                    model.Id, model.Format, model.Type,
                    string.Join(", ", _adapters.Select(a => $"{a.AdapterName}({string.Join(",", a.SupportedFormats)})")));

                throw new ModelLoadException(model.Id, errorMessage);
            }

            // Load the model using the adapter
            _logger.LogInformation("Loading model {ModelId} using adapter {AdapterName}",
                model.Id, adapter.AdapterName);

            var success = await adapter.LoadModelAsync(model, parameters, cancellationToken);

            _logger.LogInformation("Adapter {AdapterName} returned {Success} for model {ModelId}",
                adapter.AdapterName, success, model.Id);

            if (!success)
            {
                var errorMessage = "Failed to load model using adapter";
                _modelStateService.UpdateModelStatus(model.Id, ModelStatus.Failed, errorMessage);
                throw new ModelLoadException(model.Id, errorMessage);
            }

            // Update status to loaded
            _modelStateService.UpdateModelStatus(model.Id, ModelStatus.Loaded, adapterName: adapter.AdapterName);

            // Always use the actual model ID as the cache key for consistency
            _loadedModels[model.Id] = model;

            _logger.LogInformation("Model {ModelId} loaded successfully", model.Id);

            return model;
        }
        catch (Exception ex) when (!(ex is ModelLoadException))
        {
            _modelStateService.UpdateModelStatus(modelId, ModelStatus.Failed, ex.Message);
            _logger.LogError(ex, "Failed to load model {ModelId}", modelId);
            throw new ModelLoadException(modelId, "Error loading model", ex);
        }
    }

    /// <summary>
    /// Unloads a model from memory
    /// </summary>
    public async Task UnloadModelAsync(
        string modelId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get the model to resolve alias to actual ID
            var model = await _repository.GetModelAsync(modelId, cancellationToken);
            if (model == null)
            {
                _logger.LogWarning("Model {ModelId} not found for unloading", modelId);
                return;
            }

            // Update status to unloading
            _modelStateService.UpdateModelStatus(model.Id, ModelStatus.Unloading);

            // Always use the actual model ID for cache operations
            if (_loadedModels.TryRemove(model.Id, out var cachedModel))
            {
                // Find the adapter for this model
                var adapter = FindAdapter(cachedModel);
                if (adapter != null)
                {
                    // Unload from the adapter using actual model ID
                    _logger.LogInformation("Unloading model {ModelId} using adapter {AdapterName}",
                        model.Id, adapter.AdapterName);

                    var success = await adapter.UnloadModelAsync(model.Id, cancellationToken);
                    if (!success)
                    {
                        _logger.LogWarning("Adapter failed to unload model {ModelId}", model.Id);
                    }
                }

                _logger.LogInformation("Model {ModelId} unloaded successfully", model.Id);
            }
            else
            {
                // Model wasn't in our cache, but try to unload using adapter anyway
                var repoModel = await _repository.GetModelAsync(modelId, cancellationToken);
                if (repoModel != null)
                {
                    var adapter = FindAdapter(repoModel);
                    if (adapter != null)
                    {
                        _logger.LogInformation("Unloading model {ModelId} using adapter {AdapterName} (not in cache)",
                            modelId, adapter.AdapterName);

                        await adapter.UnloadModelAsync(modelId, cancellationToken);
                    }

                    _logger.LogInformation("Model {ModelId} unload attempted", modelId);
                }
            }

            // Update status to unloaded
            _modelStateService.UpdateModelStatus(model.Id, ModelStatus.Unloaded);
        }
        catch (Exception ex)
        {
            _modelStateService.UpdateModelStatus(modelId, ModelStatus.Failed, ex.Message);
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
        // Get the model from repository to resolve alias to actual ID
        var model = await _repository.GetModelAsync(modelId, cancellationToken);
        if (model == null)
        {
            return false;
        }

        // Check both our cache and the state service
        return _loadedModels.ContainsKey(model.Id) && _modelStateService.IsModelLoaded(model.Id);
    }

    /// <summary>
    /// Gets a list of currently loaded models
    /// </summary>
    public async Task<IReadOnlyList<LMModel>> GetLoadedModelsAsync(
        CancellationToken cancellationToken = default)
    {
        var loadedModels = new List<LMModel>();

        foreach (var cachedModel in _loadedModels.Values)
        {
            if (_modelStateService.IsModelLoaded(cachedModel.Id))
            {
                loadedModels.Add(cachedModel);
            }
        }

        _logger.LogInformation("Retrieved {Count} loaded models from cache", loadedModels.Count);
        return await Task.FromResult(loadedModels.AsReadOnly());
    }

    /// <summary>
    /// Gets the runtime state of a model
    /// </summary>
    public ModelRuntimeState GetModelRuntimeState(string modelId)
    {
        return _modelStateService.GetModelState(modelId);
    }

    /// <summary>
    /// Gets runtime states for all models
    /// </summary>
    public IReadOnlyDictionary<string, ModelRuntimeState> GetAllModelRuntimeStates()
    {
        return _modelStateService.GetAllModelStates();
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
    /// Initializes the service
    /// </summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Initializing RepositoryModelLoaderService");

            // No need to update repository states since we don't persist load state anymore
            // All models start as unloaded in memory state

            _logger.LogInformation("RepositoryModelLoaderService initialization completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize RepositoryModelLoaderService");
            // Don't throw here as this is initialization - the service should still be usable
        }

        return Task.CompletedTask;
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
    /// Disposes resources and cleans up
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            // Clear in-memory state only
            _loadedModels.Clear();
            _disposed = true;
        }
    }
}