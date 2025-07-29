using LMSupplyDepots.Inference.Services;
using LMSupplyDepots.Inference.Adapters;
using LMSupplyDepots.Inference.Configuration;
using LMSupplyDepots.Models;
using Microsoft.Extensions.Options;

namespace LMSupplyDepots.SDK;

/// <summary>
/// Model loading functionality for LMSupplyDepot
/// </summary>
public partial class LMSupplyDepot
{
    /// <summary>
    /// Gets the model loader that provides model loading capabilities
    /// </summary>
    private IModelLoader ModelLoader => _serviceProvider.GetRequiredService<IModelLoader>();

    /// <summary>
    /// Gets the repository model loader service for direct access to state management
    /// </summary>
    private RepositoryModelLoaderService RepositoryModelLoader => _serviceProvider.GetRequiredService<RepositoryModelLoaderService>();

    /// <summary>
    /// Gets the model state service for runtime state management
    /// </summary>
    private ModelStateService ModelStateService => _serviceProvider.GetRequiredService<ModelStateService>();

    /// <summary>
    /// Loads a model into memory
    /// </summary>
    public async Task<LMModel> LoadModelAsync(
        string modelKey,
        Dictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Resolve the model ID from the key (could be an alias)
            string modelId = await ModelManager.ResolveModelKeyAsync(modelKey, cancellationToken);
            _logger.LogInformation("Loading model {ModelId}", modelId);

            // First, check if the model exists
            var model = await ModelManager.GetModelAsync(modelId, cancellationToken);
            if (model == null)
            {
                throw new ModelLoadException(modelId, "Model not found in local repository");
            }

            // Verify the model path
            if (string.IsNullOrEmpty(model.LocalPath))
            {
                throw new ModelLoadException(modelId, "Model path is not defined");
            }

            if (!Directory.Exists(model.LocalPath) && !File.Exists(model.LocalPath))
            {
                throw new ModelLoadException(modelId, $"Model path does not exist: {model.LocalPath}");
            }

            // Check if the model directory contains valid model files
            if (Directory.Exists(model.LocalPath) && !FileSystemHelper.ContainsModelFiles(model.LocalPath))
            {
                throw new ModelLoadException(modelId,
                    $"No valid model files found in {model.LocalPath}. Expected files with extensions: {string.Join(", ", FileSystemHelper.PreferredModelFormats)}");
            }

            // Apply hardware-related parameters
            Dictionary<string, object?> loadParams = parameters ?? new Dictionary<string, object?>();
            CheckAndApplyHardwareParameters(loadParams);

            // Load the model
            var loadedModel = await ModelLoader.LoadModelAsync(modelId, loadParams, cancellationToken);
            
            var runtimeState = ModelStateService.GetModelState(modelId);
            _logger.LogInformation("Model {ModelId} loaded successfully at {LoadedAt}", 
                modelId, runtimeState.LoadedAt);

            return loadedModel;
        }
        catch (ModelLoadException)
        {
            // Re-throw existing model load exceptions
            throw;
        }
        catch (Exception ex)
        {
            string modelId;
            try
            {
                modelId = await ModelManager.ResolveModelKeyAsync(modelKey, cancellationToken);
            }
            catch
            {
                modelId = modelKey; // Fallback to the original key if resolution fails
            }

            _logger.LogError(ex, "Failed to load model {ModelId}", modelId);
            throw new ModelLoadException(modelId, $"Error loading model: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Unloads a model from memory
    /// </summary>
    public async Task UnloadModelAsync(
        string modelKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Resolve the model ID from the key (could be an alias)
            string modelId = await ModelManager.ResolveModelKeyAsync(modelKey, cancellationToken);
            _logger.LogInformation("Unloading model {ModelId}", modelId);

            // Remove from engine caches
            _textGenerationEngines.TryRemove(modelId, out var textEngine);
            if (textEngine is IDisposable disposableTextEngine)
            {
                disposableTextEngine.Dispose();
            }

            _embeddingEngines.TryRemove(modelId, out var embedEngine);
            if (embedEngine is IDisposable disposableEmbedEngine)
            {
                disposableEmbedEngine.Dispose();
            }

            // Unload from model loader
            await ModelLoader.UnloadModelAsync(modelId, cancellationToken);

            _logger.LogInformation("Model {ModelId} unloaded successfully", modelId);
        }
        catch (Exception ex)
        {
            string modelId;
            try
            {
                modelId = await ModelManager.ResolveModelKeyAsync(modelKey, cancellationToken);
            }
            catch
            {
                modelId = modelKey; // Fallback to the original key if resolution fails
            }

            _logger.LogError(ex, "Failed to unload model {ModelId}", modelId);
            throw;
        }
    }

    /// <summary>
    /// Checks if a model is loaded
    /// </summary>
    public async Task<bool> IsModelLoadedAsync(
        string modelKey,
        CancellationToken cancellationToken = default)
    {
        string modelId = await ModelManager.ResolveModelKeyAsync(modelKey, cancellationToken);
        return await ModelLoader.IsModelLoadedAsync(modelId, cancellationToken);
    }

    /// <summary>
    /// Gets a list of loaded models
    /// </summary>
    public Task<IReadOnlyList<LMModel>> GetLoadedModelsAsync(
        CancellationToken cancellationToken = default)
    {
        return ModelLoader.GetLoadedModelsAsync(cancellationToken);
    }

    /// <summary>
    /// Gets the runtime state of a model
    /// </summary>
    public async Task<ModelRuntimeState> GetModelRuntimeStateAsync(
        string modelKey,
        CancellationToken cancellationToken = default)
    {
        string modelId = await ModelManager.ResolveModelKeyAsync(modelKey, cancellationToken);
        return RepositoryModelLoader.GetModelRuntimeState(modelId);
    }

    /// <summary>
    /// Gets runtime states for all models
    /// </summary>
    public IReadOnlyDictionary<string, ModelRuntimeState> GetAllModelRuntimeStates()
    {
        return RepositoryModelLoader.GetAllModelRuntimeStates();
    }

    /// <summary>
    /// Checks and applies hardware-related parameters based on environment
    /// </summary>
    private void CheckAndApplyHardwareParameters(Dictionary<string, object?> parameters)
    {
        // If GpuLayers already specified, respect that setting
        if (parameters.ContainsKey("gpu_layers"))
        {
            return;
        }

        // If LLamaOptions has GpuLayers value, check hardware compatibility
        if (_options.LLamaOptions?.GpuLayers > 0)
        {
            // Check for hardware acceleration capabilities
            // Note: For more accurate detection, we should use the LLamaBackendService directly,
            // but for simplicity we'll just check environment variables here

            bool gpuAvailable = false;

            // Check for CUDA availability
            var cudaDevices = Environment.GetEnvironmentVariable("CUDA_VISIBLE_DEVICES");
            if (!string.IsNullOrEmpty(cudaDevices))
            {
                gpuAvailable = true;
            }

            // Check for Vulkan 
            var vulkanEnabled = Environment.GetEnvironmentVariable("LLAMA_VULKAN");
            if (!string.IsNullOrEmpty(vulkanEnabled) && vulkanEnabled.Equals("1", StringComparison.OrdinalIgnoreCase))
            {
                gpuAvailable = true;
            }

            // Check for Metal on macOS
            if (OperatingSystem.IsMacOS())
            {
                gpuAvailable = true;
            }

            // Set gpu_layers to 0 if GPU is not available
            if (!gpuAvailable)
            {
                _logger.LogWarning("No hardware acceleration detected. Setting gpu_layers to 0");
                parameters["gpu_layers"] = 0;
            }
            else
            {
                parameters["gpu_layers"] = _options.LLamaOptions.GpuLayers;
            }
        }

        // Apply other hardware parameters
        if (_options.LLamaOptions?.Threads > 0 && !parameters.ContainsKey("threads"))
        {
            parameters["threads"] = _options.LLamaOptions.Threads;
        }

        if (_options.LLamaOptions?.BatchSize > 0 && !parameters.ContainsKey("batch_size"))
        {
            parameters["batch_size"] = _options.LLamaOptions.BatchSize;
        }

        if (_options.LLamaOptions?.ContextSize > 0 && !parameters.ContainsKey("context_size"))
        {
            parameters["context_size"] = _options.LLamaOptions.ContextSize;
        }
    }

    /// <summary>
    /// Configures model loader services
    /// </summary>
    private void ConfigureModelLoaderServices(IServiceCollection services)
    {
        _logger.LogInformation("Configuring ModelLoader services...");

        // Register ModelStateService as a singleton
        services.AddSingleton<ModelStateService>();

        // Register the RepositoryModelLoaderService that uses adapters to actually load models into engines
        services.AddSingleton<RepositoryModelLoaderService>(sp =>
        {
            var repository = sp.GetRequiredService<IModelRepository>();
            var logger = sp.GetRequiredService<ILogger<RepositoryModelLoaderService>>();
            var adapters = sp.GetServices<BaseModelAdapter>().ToList();
            var modelStateService = sp.GetRequiredService<ModelStateService>();

            logger.LogInformation("Creating RepositoryModelLoaderService with {AdapterCount} adapters", adapters.Count);
            foreach (var adapter in adapters)
            {
                logger.LogInformation("Found adapter: {AdapterName} ({AdapterType})", adapter.AdapterName, adapter.GetType().Name);
            }

            return new RepositoryModelLoaderService(repository, logger, adapters, modelStateService);
        });

        services.AddSingleton<IModelLoader>(sp => sp.GetRequiredService<RepositoryModelLoaderService>());

        _logger.LogInformation("ModelLoader services configured");
    }

    /// <summary>
    /// Initializes the model loader
    /// </summary>
    private async Task InitializeModelLoaderAsync()
    {
        try
        {
            _logger.LogInformation("Initializing model loader");
            await RepositoryModelLoader.InitializeAsync(CancellationToken.None);
            _logger.LogInformation("Model loader initialization completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize model loader");
            // Don't throw here as this is initialization - the SDK should still be usable
        }
    }
}