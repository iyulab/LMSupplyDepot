using LMSupplyDepots.External.LLamaEngine.Models;
using LMSupplyDepots.External.LLamaEngine.Services;
using LMSupplyDepots.Inference.Engines.Embedding;
using LMSupplyDepots.Inference.Engines.TextGeneration;
using Microsoft.Extensions.DependencyInjection;

namespace LMSupplyDepots.Inference.Adapters;

/// <summary>
/// Adapter for LLama engine that connects to LMSupplyDepots.External.LLamaEngine
/// </summary>
public class LLamaAdapter : BaseModelAdapter
{
    private readonly ILLamaModelManager _modelManager;
    private readonly ILLMService _llmService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<string, LMModel> _loadedModels = new();

    /// <summary>
    /// Name of the adapter
    /// </summary>
    public override string AdapterName => "LLama";

    /// <summary>
    /// The model format(s) this adapter supports
    /// </summary>
    public override IReadOnlyList<string> SupportedFormats => new[] { "GGUF", "GGML" };

    /// <summary>
    /// Model types supported by this adapter
    /// </summary>
    public override IReadOnlyList<ModelType> SupportedModelTypes => new[]
    {
        ModelType.TextGeneration,
        ModelType.Embedding
    };

    /// <summary>
    /// Initializes a new instance of the LLama adapter
    /// </summary>
    public LLamaAdapter(
        ILogger<LLamaAdapter> logger,
        ILLamaModelManager modelManager,
        ILLMService llmService,
        IServiceProvider serviceProvider)
        : base(logger)
    {
        _modelManager = modelManager;
        _llmService = llmService;
        _serviceProvider = serviceProvider;

        // Subscribe to model state change events
        _modelManager.ModelStateChanged += OnModelStateChanged;
    }

    /// <summary>
    /// Loads a model into memory
    /// </summary>
    public override async Task<bool> LoadModelAsync(
        LMModel model,
        Dictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("LLamaAdapter.LoadModelAsync called for model {ModelId}", model.Id);

        ThrowIfDisposed();

        if (!CanHandle(model))
        {
            _logger.LogWarning("Model {ModelId} not supported by this adapter", model.Id);
            return false;
        }

        try
        {
            _logger.LogInformation("Starting model loading process for {ModelId}", model.Id);

            // Store the model in our local tracking
            _loadedModels[model.Id] = model;

            // Check if model is already loaded in LLama engine
            _logger.LogInformation("Checking if model {ModelId} is already loaded in LLama engine", model.Id);
            var llmInfo = await _modelManager.GetModelInfoAsync(model.Id);
            if (llmInfo?.State == LocalModelState.Loaded)
            {
                _logger.LogInformation("Model {ModelId} is already loaded", model.Id);
                return true;
            }

            // Load the model into LLama engine
            if (string.IsNullOrEmpty(model.LocalPath))
            {
                throw new ModelLoadException(model.Id, "Model path is not defined");
            }

            // Verify the model file exists
            string modelFilePath = model.LocalPath;
            if (Directory.Exists(model.LocalPath))
            {
                // If it's a directory, look for model files
                var files = Directory.GetFiles(model.LocalPath, "*.gguf", SearchOption.TopDirectoryOnly)
                    .Concat(Directory.GetFiles(model.LocalPath, "*.ggml", SearchOption.TopDirectoryOnly))
                    .ToList();

                if (files.Count == 0)
                {
                    throw new ModelLoadException(model.Id,
                        $"No model files found in directory {model.LocalPath}");
                }

                // Use the largest file as the model file if multiple files found
                modelFilePath = files.OrderByDescending(f => new FileInfo(f).Length).First();
                _logger.LogInformation("Using model file {ModelPath} for model {ModelId}",
                    modelFilePath, model.Id);
            }
            else if (!File.Exists(model.LocalPath))
            {
                // If specific model file is specified but doesn't exist
                throw new ModelLoadException(model.Id,
                    $"Model file not found at {model.LocalPath}");
            }

            _logger.LogInformation("Loading model {ModelId} from {ModelPath}", model.Id, modelFilePath);

            // Apply parameters if provided
            Dictionary<string, object?> loadingParameters = parameters ?? new Dictionary<string, object?>();

            // Create a merged parameters dictionary for debugging
            var debugParams = new Dictionary<string, object?>(loadingParameters);
            _logger.LogDebug("Loading model {ModelId} with parameters: {Parameters}",
                model.Id, string.Join(", ", debugParams.Select(p => $"{p.Key}={p.Value}")));

            try
            {
                _logger.LogInformation("Calling LLamaModelManager.LoadModelAsync for model {ModelId} from path {ModelPath}", model.Id, modelFilePath);

                // 수정된 부분: LoadModelAsync 메서드는 2개의 인자만 받도록 수정
                var loadedModel = await _modelManager.LoadModelAsync(modelFilePath, model.Id);

                _logger.LogInformation("LLamaModelManager.LoadModelAsync returned for model {ModelId}. State: {State}, LastError: {LastError}",
                    model.Id, loadedModel?.State, loadedModel?.LastError);

                // Check if loaded successfully
                if (loadedModel?.State != LocalModelState.Loaded)
                {
                    var error = loadedModel?.LastError ?? "Unknown error";
                    throw new ModelLoadException(model.Id, $"Failed to load model: {error}");
                }

                _logger.LogInformation("Model {ModelId} loaded successfully", model.Id);
                return true;
            }
            catch (Exception ex) when (ex is not ModelLoadException)
            {
                _logger.LogError(ex, "Exception occurred while calling LLamaModelManager.LoadModelAsync for model {ModelId}", model.Id);
                throw new ModelLoadException(model.Id, $"Error loading model: {ex.Message}", ex);
            }
        }
        catch (ModelLoadException)
        {
            // Re-throw existing model load exceptions
            _loadedModels.TryRemove(model.Id, out _);
            throw;
        }
        catch (Exception ex)
        {
            _loadedModels.TryRemove(model.Id, out _);
            throw new ModelLoadException(model.Id, "Error loading model", ex);
        }
    }

    /// <summary>
    /// Unloads a model from memory
    /// </summary>
    public override async Task<bool> UnloadModelAsync(
        string modelId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            _loadedModels.TryRemove(modelId, out _);

            // Check if model is loaded
            var llmInfo = await _modelManager.GetModelInfoAsync(modelId);
            if (llmInfo?.State != LocalModelState.Loaded)
            {
                _logger.LogInformation("Model {ModelId} is not loaded", modelId);
                return true;
            }

            // Unload the model
            await _modelManager.UnloadModelAsync(modelId);
            _logger.LogInformation("Model {ModelId} unloaded successfully", modelId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unloading model {ModelId}", modelId);
            return false;
        }
    }

    /// <summary>
    /// Checks if a model is currently loaded
    /// </summary>
    public override async Task<bool> IsModelLoadedAsync(
        string modelId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var llmInfo = await _modelManager.GetModelInfoAsync(modelId);
        return llmInfo?.State == LocalModelState.Loaded;
    }

    /// <summary>
    /// Creates a text generation engine for a loaded model
    /// </summary>
    public override async Task<ITextGenerationEngine?> CreateTextGenerationEngineAsync(
        string modelId,
        Dictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // Check if model is loaded
        if (!await IsModelLoadedAsync(modelId, cancellationToken))
        {
            _logger.LogWarning("Cannot create text generation engine: Model {ModelId} is not loaded", modelId);
            return null;
        }

        // Check if model supports text generation
        if (!_loadedModels.TryGetValue(modelId, out var model) ||
            !model.Capabilities.SupportsTextGeneration)
        {
            _logger.LogWarning("Model {ModelId} does not support text generation", modelId);
            return null;
        }

        try
        {
            // Create the engine
            var engine = ActivatorUtilities.CreateInstance<LLamaTextGenerationEngine>(
                _serviceProvider,
                _llmService,
                modelId,
                parameters ?? new Dictionary<string, object?>());

            _logger.LogInformation("Created text generation engine for model {ModelId}", modelId);
            return engine;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating text generation engine for model {ModelId}", modelId);
            return null;
        }
    }

    /// <summary>
    /// Creates an embedding engine for a loaded model
    /// </summary>
    public override async Task<IEmbeddingEngine?> CreateEmbeddingEngineAsync(
        string modelId,
        Dictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // Check if model is loaded
        if (!await IsModelLoadedAsync(modelId, cancellationToken))
        {
            _logger.LogWarning("Cannot create embedding engine: Model {ModelId} is not loaded", modelId);
            return null;
        }

        // Check if model supports embeddings
        if (!_loadedModels.TryGetValue(modelId, out var model) ||
            !model.Capabilities.SupportsEmbeddings)
        {
            _logger.LogWarning("Model {ModelId} does not support embeddings", modelId);
            return null;
        }

        try
        {
            // Create the engine
            var engine = ActivatorUtilities.CreateInstance<LLamaEmbeddingEngine>(
                _serviceProvider,
                _llmService,
                modelId,
                parameters ?? new Dictionary<string, object?>());

            _logger.LogInformation("Created embedding engine for model {ModelId}", modelId);
            return engine;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating embedding engine for model {ModelId}", modelId);
            return null;
        }
    }

    /// <summary>
    /// Called when model state changes
    /// </summary>
    private void OnModelStateChanged(object? sender, ModelStateChangedEventArgs e)
    {
        _logger.LogInformation("Model {ModelId} state changed to {NewState}", e.ModelIdentifier, e.NewState);

        if (e.NewState == LocalModelState.Failed || e.NewState == LocalModelState.Unloaded)
        {
            // Remove from our local tracking
            _loadedModels.TryRemove(e.ModelIdentifier, out _);
        }
    }

    /// <summary>
    /// Disposes resources
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            // Unsubscribe from events
            _modelManager.ModelStateChanged -= OnModelStateChanged;

            // Unload all models
            foreach (var modelId in _loadedModels.Keys)
            {
                try
                {
                    _modelManager.UnloadModelAsync(modelId).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error unloading model {ModelId} during disposal", modelId);
                }
            }

            _loadedModels.Clear();
        }

        base.Dispose(disposing);
    }
}