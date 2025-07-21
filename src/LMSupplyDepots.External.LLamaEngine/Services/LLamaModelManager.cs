using LLama;
using LMSupplyDepots.External.LLamaEngine.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace LMSupplyDepots.External.LLamaEngine.Services;

public interface ILLamaModelManager
{
    event EventHandler<ModelStateChangedEventArgs>? ModelStateChanged;
    Task<LocalModelInfo?> LoadModelAsync(string filePath, string modelIdentifier);
    Task UnloadModelAsync(string modelIdentifier);
    LLamaWeights? GetModelWeights(string modelIdentifier);
    ModelConfig? GetModelConfig(string modelIdentifier);
    Task<IReadOnlyList<LocalModelInfo>> GetLoadedModelsAsync();
    Task<LocalModelInfo?> GetModelInfoAsync(string modelIdentifier);
    string NormalizeModelIdentifier(string modelIdentifier);
}

public class LLamaModelManager(ILogger<LLamaModelManager> logger, ILLamaBackendService backendService) : ILLamaModelManager
{
    private readonly ILogger<LLamaModelManager> _logger = logger;
    private readonly ConcurrentDictionary<string, LocalModelInfo> _localModels = new();
    private readonly ConcurrentDictionary<string, ModelResources> _modelResources = new();
    private readonly ConcurrentDictionary<string, ModelConfig> _modelConfigs = new();
    private readonly ILLamaBackendService _backendService = backendService;

    public event EventHandler<ModelStateChangedEventArgs>? ModelStateChanged;

    public string NormalizeModelIdentifier(string modelIdentifier)
    {
        if (modelIdentifier.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
        {
            return modelIdentifier[..^5];
        }
        return modelIdentifier;
    }

    public async Task<LocalModelInfo?> LoadModelAsync(string filePath, string modelIdentifier)
    {
        modelIdentifier = NormalizeModelIdentifier(modelIdentifier);
        var modelInfo = LocalModelInfo.CreateFromIdentifier(filePath, modelIdentifier);

        if (_localModels.TryGetValue(modelIdentifier, out var existingInfo) &&
            existingInfo.State == LocalModelState.Loaded)
        {
            return existingInfo;
        }

        UpdateModelState(modelInfo, LocalModelState.Loading);

        try
        {
            // Load model configuration
            var config = modelInfo.LoadConfig(_logger);
            _modelConfigs[modelIdentifier] = config;

            // Get optimal parameters based on configuration
            var parameters = _backendService.GetOptimalModelParams(filePath, config);

            // Load weights with configured parameters
            var weights = await LLamaWeights.LoadFromFileAsync(parameters);
            var resources = new ModelResources(weights);
            _modelResources[modelIdentifier] = resources;

            UpdateModelState(modelInfo, LocalModelState.Loaded);
            return modelInfo;
        }
        catch (Exception ex)
        {
            modelInfo.LastError = ex.Message;
            UpdateModelState(modelInfo, LocalModelState.Failed);
            throw;
        }
    }

    public ModelConfig? GetModelConfig(string modelIdentifier)
    {
        _modelConfigs.TryGetValue(modelIdentifier, out var config);
        return config;
    }

    public LLamaWeights? GetModelWeights(string modelIdentifier)
    {
        if (_modelResources.TryGetValue(modelIdentifier, out var resources))
        {
            return resources.Weights;
        }
        return null;
    }

    private void UpdateModelState(LocalModelInfo modelInfo, LocalModelState newState)
    {
        var oldState = modelInfo.State;
        modelInfo.State = newState;
        _localModels[modelInfo.ModelId] = modelInfo;

        ModelStateChanged?.Invoke(this, new ModelStateChangedEventArgs(
            modelInfo.ModelId,
            oldState,
            newState));

        if (newState == LocalModelState.Unloaded)
        {
            if (_modelResources.TryRemove(modelInfo.ModelId, out var resources))
            {
                resources.Dispose();
            }
            _modelConfigs.TryRemove(modelInfo.ModelId, out _);
        }
    }

    public Task UnloadModelAsync(string modelIdentifier)
    {
        modelIdentifier = NormalizeModelIdentifier(modelIdentifier);

        if (_localModels.TryGetValue(modelIdentifier, out var modelInfo))
        {
            try
            {
                UpdateModelState(modelInfo, LocalModelState.Unloading);
                UpdateModelState(modelInfo, LocalModelState.Unloaded);

                _logger.LogInformation("Successfully unloaded model {ModelIdentifier}", modelIdentifier);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to unload model {ModelIdentifier}", modelIdentifier);
                modelInfo.LastError = ex.Message;
                UpdateModelState(modelInfo, LocalModelState.Failed);
                throw;
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LocalModelInfo>> GetLoadedModelsAsync()
    {
        var loadedModels = _localModels.Values
            .Where(m => m.State == LocalModelState.Loaded)
            .ToList();

        return Task.FromResult<IReadOnlyList<LocalModelInfo>>(loadedModels);
    }

    public Task<LocalModelInfo?> GetModelInfoAsync(string modelIdentifier)
    {
        modelIdentifier = NormalizeModelIdentifier(modelIdentifier);
        _localModels.TryGetValue(modelIdentifier, out var modelInfo);
        return Task.FromResult(modelInfo);
    }
}