using LMSupplyDepots.Contracts;
using LMSupplyDepots.Inference;
using LMSupplyDepots.Inference.Adapters;
using LMSupplyDepots.Inference.Configuration;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace LMSupplyDepots.SDK;

/// <summary>
/// Inference functionality for LMSupplyDepot
/// </summary>
public partial class LMSupplyDepot
{
    private readonly ConcurrentDictionary<string, ITextGenerationEngine> _textGenerationEngines = new();
    private readonly ConcurrentDictionary<string, IEmbeddingEngine> _embeddingEngines = new();

    /// <summary>
    /// Generates text using a loaded model
    /// </summary>
    public async Task<GenerationResponse> GenerateTextAsync(
        string modelKey,
        string prompt,
        int maxTokens = 256,
        float temperature = 0.7f,
        float topP = 0.95f,
        Dictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var request = new GenerationRequest
        {
            Prompt = prompt,
            MaxTokens = maxTokens,
            Temperature = temperature,
            TopP = topP,
            Stream = false,
            Parameters = parameters ?? new Dictionary<string, object?>()
        };

        return await GenerateTextAsync(modelKey, request, cancellationToken);
    }

    /// <summary>
    /// Generates text using a loaded model with a full request
    /// </summary>
    public async Task<GenerationResponse> GenerateTextAsync(
        string modelKey,
        GenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            string modelId = await ModelManager.ResolveModelKeyAsync(modelKey, cancellationToken);
            var engine = await GetTextGenerationEngineAsync(modelId, cancellationToken);
            return await engine.GenerateAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not GenerationException)
        {
            string modelId;
            try
            {
                modelId = await ModelManager.ResolveModelKeyAsync(modelKey, cancellationToken);
            }
            catch
            {
                modelId = modelKey;
            }

            _logger.LogError(ex, "Error generating text with model {ModelId}", modelId);
            throw new GenerationException(modelId, "Text generation failed", ex);
        }
    }

    /// <summary>
    /// Generates text using a loaded model with streaming output
    /// </summary>
    public async IAsyncEnumerable<string> GenerateTextStreamAsync(
        string modelKey,
        string prompt,
        int maxTokens = 256,
        float temperature = 0.7f,
        float topP = 0.95f,
        Dictionary<string, object?>? parameters = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = new GenerationRequest
        {
            Prompt = prompt,
            MaxTokens = maxTokens,
            Temperature = temperature,
            TopP = topP,
            Stream = true,
            Parameters = parameters ?? new Dictionary<string, object?>()
        };

        string modelId = await ModelManager.ResolveModelKeyAsync(modelKey, cancellationToken);
        var engine = await GetTextGenerationEngineAsync(modelId, cancellationToken);

        await foreach (var token in engine.GenerateStreamAsync(request, cancellationToken))
        {
            yield return token;
        }
    }

    /// <summary>
    /// Generates embeddings for texts using a loaded model
    /// </summary>
    public async Task<EmbeddingResponse> GenerateEmbeddingsAsync(
        string modelKey,
        IReadOnlyList<string> texts,
        bool normalize = false,
        Dictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var request = new EmbeddingRequest
        {
            Texts = texts,
            Normalize = normalize,
            Parameters = parameters ?? new Dictionary<string, object?>()
        };

        return await GenerateEmbeddingsAsync(modelKey, request, cancellationToken);
    }

    /// <summary>
    /// Generates embeddings for texts using a loaded model with a full request
    /// </summary>
    public async Task<EmbeddingResponse> GenerateEmbeddingsAsync(
        string modelKey,
        EmbeddingRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            string modelId = await ModelManager.ResolveModelKeyAsync(modelKey, cancellationToken);
            var engine = await GetEmbeddingEngineAsync(modelId, cancellationToken);
            return await engine.GenerateEmbeddingsAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not GenerationException)
        {
            string modelId;
            try
            {
                modelId = await ModelManager.ResolveModelKeyAsync(modelKey, cancellationToken);
            }
            catch
            {
                modelId = modelKey;
            }

            _logger.LogError(ex, "Error generating embeddings with model {ModelId}", modelId);
            throw new GenerationException(modelId, "Embedding generation failed", ex);
        }
    }

    /// <summary>
    /// Gets or creates a text generation engine for a loaded model
    /// </summary>
    private async Task<ITextGenerationEngine> GetTextGenerationEngineAsync(
        string modelKey,
        CancellationToken cancellationToken = default)
    {
        string modelId = await ModelManager.ResolveModelKeyAsync(modelKey, cancellationToken);

        // Try to get an existing engine
        if (_textGenerationEngines.TryGetValue(modelId, out var engine))
        {
            return engine;
        }

        // Ensure the model is loaded
        if (!await IsModelLoadedAsync(modelId, cancellationToken))
        {
            _logger.LogInformation("Model {ModelId} is not loaded, loading now", modelId);
            try
            {
                await LoadModelAsync(modelId, null, cancellationToken);
            }
            catch (ModelLoadException ex)
            {
                // Add more context to the exception with potential fixes
                string additionalInfo = GetModelLoadErrorDetails(ex, modelId);
                throw new ModelLoadException(modelId, $"{ex.Message}. {additionalInfo}", ex.InnerException);
            }
        }

        // Find a suitable adapter for this model
        var model = await ModelManager.GetModelAsync(modelId, cancellationToken);
        if (model == null)
        {
            throw new ModelLoadException(modelId, "Model not found");
        }

        // Verify the model file exists
        if (string.IsNullOrEmpty(model.LocalPath) || !Directory.Exists(model.LocalPath))
        {
            throw new ModelLoadException(modelId, $"Model path is invalid or not accessible: {model.LocalPath}");
        }

        // Verify the model file path contains actual model files
        if (!FileSystemHelper.ContainsModelFiles(model.LocalPath))
        {
            string modelDir = model.LocalPath;
            throw new ModelLoadException(modelId,
                $"No valid model files found in {modelDir}. Expected files with extensions: {string.Join(", ", FileSystemHelper.PreferredModelFormats)}");
        }

        var adapters = _serviceProvider.GetServices<BaseModelAdapter>();
        bool anyAdapterFound = false;

        foreach (var adapter in adapters)
        {
            if (adapter.CanHandle(model))
            {
                anyAdapterFound = true;

                // Try to create the engine with the suitable adapter
                try
                {
                    // Create the parameters
                    var engineParams = _options.TextGeneration?.ToParameters() ?? new Dictionary<string, object?>();

                    // Create a text generation engine
                    engine = await adapter.CreateTextGenerationEngineAsync(
                        modelId,
                        engineParams,
                        cancellationToken);

                    if (engine != null)
                    {
                        _textGenerationEngines[modelId] = engine;
                        return engine;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error creating text generation engine with adapter {AdapterName} for model {ModelId}",
                        adapter.AdapterName, modelId);
                }
            }
        }

        if (!anyAdapterFound)
        {
            throw new GenerationException(modelId,
                $"No suitable adapter found for model format '{model.Format}' and type '{model.Type}'");
        }

        throw new GenerationException(modelId,
            "Failed to create text generation engine. Check model format and compatibility.");
    }

    /// <summary>
    /// Gets or creates an embedding engine for a loaded model
    /// </summary>
    private async Task<IEmbeddingEngine> GetEmbeddingEngineAsync(
        string modelKey,
        CancellationToken cancellationToken = default)
    {
        string modelId = await ModelManager.ResolveModelKeyAsync(modelKey, cancellationToken);

        // Try to get an existing engine
        if (_embeddingEngines.TryGetValue(modelId, out var engine))
        {
            return engine;
        }

        // Ensure the model is loaded
        if (!await IsModelLoadedAsync(modelId, cancellationToken))
        {
            _logger.LogInformation("Model {ModelId} is not loaded, loading now", modelId);
            try
            {
                await LoadModelAsync(modelId, null, cancellationToken);
            }
            catch (ModelLoadException ex)
            {
                // Add more context to the exception with potential fixes
                string additionalInfo = GetModelLoadErrorDetails(ex, modelId);
                throw new ModelLoadException(modelId, $"{ex.Message}. {additionalInfo}", ex.InnerException);
            }
        }

        // Find a suitable adapter for this model
        var model = await ModelManager.GetModelAsync(modelId, cancellationToken);
        if (model == null)
        {
            throw new ModelLoadException(modelId, "Model not found");
        }

        // Verify the model path exists
        if (string.IsNullOrEmpty(model.LocalPath) || !Directory.Exists(model.LocalPath))
        {
            throw new ModelLoadException(modelId, $"Model path is invalid or not accessible: {model.LocalPath}");
        }

        // Verify the model file path contains actual model files
        if (!FileSystemHelper.ContainsModelFiles(model.LocalPath))
        {
            string modelDir = model.LocalPath;
            throw new ModelLoadException(modelId,
                $"No valid model files found in {modelDir}. Expected files with extensions: {string.Join(", ", FileSystemHelper.PreferredModelFormats)}");
        }

        var adapters = _serviceProvider.GetServices<BaseModelAdapter>();
        bool anyAdapterFound = false;

        foreach (var adapter in adapters)
        {
            if (adapter.CanHandle(model))
            {
                anyAdapterFound = true;

                // Try to create the engine with the suitable adapter
                try
                {
                    // Create the parameters
                    var engineParams = _options.Embedding?.ToParameters() ?? new Dictionary<string, object?>();

                    // Create an embedding engine
                    engine = await adapter.CreateEmbeddingEngineAsync(
                        modelId,
                        engineParams,
                        cancellationToken);

                    if (engine != null)
                    {
                        _embeddingEngines[modelId] = engine;
                        return engine;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error creating embedding engine with adapter {AdapterName} for model {ModelId}",
                        adapter.AdapterName, modelId);
                }
            }
        }

        if (!anyAdapterFound)
        {
            throw new GenerationException(modelId,
                $"No suitable adapter found for model format '{model.Format}' and type '{model.Type}'");
        }

        throw new GenerationException(modelId,
            "Failed to create embedding engine. Check if the model supports embeddings.");
    }

    /// <summary>
    /// Gets additional details for model loading errors to help troubleshoot
    /// </summary>
    private string GetModelLoadErrorDetails(ModelLoadException ex, string modelId)
    {
        // Determine the type of error and provide helpful context
        string message = ex.Message.ToLowerInvariant();

        if (message.Contains("path is not defined") || message.Contains("not found") || message.Contains("invalid path"))
        {
            return "Check that the model has been properly downloaded and the file path is correct";
        }

        if (message.Contains("failed to load model"))
        {
            if (message.Contains("hardware") || message.Contains("gpu"))
            {
                return "Hardware acceleration failed. Try setting GpuLayers to 0 to use CPU only";
            }

            return "Verify that the model file is not corrupted and is in a supported format";
        }

        if (message.Contains("memory") || message.Contains("allocation"))
        {
            return "The system may not have enough memory to load this model. Try a smaller model or reduce batch size";
        }

        // Default message
        return "Verify that the model is properly downloaded and in a supported format";
    }

    /// <summary>
    /// Configures inference services
    /// </summary>
    private void ConfigureInferenceServices(IServiceCollection services)
    {
        // Add core inference services
        services.AddInferenceServices(options =>
        {
            options.DefaultTimeoutMs = _options.DefaultTimeoutMs;
            options.MaxConcurrentOperations = _options.MaxConcurrentOperations;
            options.EnableMetrics = _options.EnableMetrics;
            options.EnableModelCaching = _options.EnableModelCaching;
            options.MaxCachedModels = _options.MaxCachedModels;
            options.TempDirectory = _options.TempDirectory;

            // Add engine-specific options
            if (_options.LLamaOptions != null)
            {
                options.EngineOptions["LLama"] = new EngineOptions
                {
                    Enabled = true,
                    Priority = 10, // Higher priority (lower number)
                    Parameters = _options.LLamaOptions.ToParameters()
                };
            }
        });

        // Add LLama backend
        services.AddLLamaBackend();
    }
}