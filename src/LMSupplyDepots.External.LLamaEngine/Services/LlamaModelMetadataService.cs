using LLama;
using LMSupplyDepots.External.LLamaEngine.Services;
using LMSupplyDepots.SDK.Services;
using Microsoft.Extensions.Logging;

namespace LMSupplyDepots.External.LLamaEngine.Services;

/// <summary>
/// Implementation of model metadata service using LlamaSharp
/// </summary>
public class LlamaModelMetadataService : IModelMetadataService
{
    private readonly ILogger<LlamaModelMetadataService> _logger;
    private readonly ModelMetadataExtractor _metadataExtractor;
    private readonly DynamicChatTemplateService _chatTemplateService;
    private readonly Dictionary<string, SafeLlamaModelHandle> _modelHandles;
    private readonly Dictionary<string, LMSupplyDepots.SDK.Services.ModelMetadata> _metadataCache;

    public LlamaModelMetadataService(
        ILogger<LlamaModelMetadataService> logger,
        ModelMetadataExtractor metadataExtractor,
        DynamicChatTemplateService chatTemplateService)
    {
        _logger = logger;
        _metadataExtractor = metadataExtractor;
        _chatTemplateService = chatTemplateService;
        _modelHandles = new Dictionary<string, SafeLlamaModelHandle>();
        _metadataCache = new Dictionary<string, LMSupplyDepots.SDK.Services.ModelMetadata>();
    }

    /// <summary>
    /// Register a model handle for metadata extraction
    /// </summary>
    public void RegisterModelHandle(string modelId, SafeLlamaModelHandle modelHandle)
    {
        _modelHandles[modelId] = modelHandle;

        // Clear cached metadata when model handle changes
        if (_metadataCache.ContainsKey(modelId))
        {
            _metadataCache.Remove(modelId);
        }
    }

    /// <summary>
    /// Unregister a model handle
    /// </summary>
    public void UnregisterModelHandle(string modelId)
    {
        _modelHandles.Remove(modelId);
        _metadataCache.Remove(modelId);
    }

    /// <inheritdoc/>
    public async Task<LMSupplyDepots.SDK.Services.ModelMetadata> GetModelMetadataAsync(
        string modelId,
        CancellationToken cancellationToken = default)
    {
        // Check cache first
        if (_metadataCache.TryGetValue(modelId, out var cachedMetadata))
        {
            return cachedMetadata;
        }

        // Get model handle
        if (!_modelHandles.TryGetValue(modelId, out var modelHandle))
        {
            throw new InvalidOperationException($"Model '{modelId}' is not registered for metadata extraction");
        }

        try
        {
            // Extract metadata using LlamaSharp
            var llamaMetadata = _metadataExtractor.ExtractMetadata(modelHandle);

            // Convert to SDK format
            var sdkMetadata = ConvertToSdkMetadata(llamaMetadata);

            // Cache the result
            _metadataCache[modelId] = sdkMetadata;

            _logger.LogInformation("Successfully extracted metadata for model: {ModelId}", modelId);
            return sdkMetadata;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract metadata for model: {ModelId}", modelId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<string> ApplyChatTemplateAsync(
        string modelId,
        IEnumerable<LMSupplyDepots.SDK.Services.ChatMessage> messages,
        bool addGenerationPrompt = true,
        LMSupplyDepots.SDK.Services.ToolCallOptions? toolOptions = null,
        CancellationToken cancellationToken = default)
    {
        // Get model handle
        if (!_modelHandles.TryGetValue(modelId, out var modelHandle))
        {
            throw new InvalidOperationException($"Model '{modelId}' is not registered for chat template application");
        }

        try
        {
            // Convert SDK messages to LLamaEngine format
            var llamaMessages = messages.Select(m => new LMSupplyDepots.External.LLamaEngine.Services.ChatMessage
            {
                Role = m.Role,
                Content = m.Content
            }).ToArray();

            // Convert tool options
            LMSupplyDepots.External.LLamaEngine.Services.ToolCallOptions? llamaToolOptions = null;
            if (toolOptions != null)
            {
                llamaToolOptions = new LMSupplyDepots.External.LLamaEngine.Services.ToolCallOptions
                {
                    Tools = toolOptions.Tools.Select(t => new LMSupplyDepots.External.LLamaEngine.Services.ToolDefinition
                    {
                        Name = t.Name,
                        Description = t.Description,
                        Parameters = t.Parameters
                    })
                };
            }

            // Apply chat template
            var result = _chatTemplateService.ApplyChatTemplate(
                modelHandle,
                llamaMessages,
                addGenerationPrompt,
                llamaToolOptions);

            _logger.LogDebug("Successfully applied chat template for model: {ModelId}", modelId);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply chat template for model: {ModelId}", modelId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> SupportsToolCallingAsync(string modelId, CancellationToken cancellationToken = default)
    {
        var metadata = await GetModelMetadataAsync(modelId, cancellationToken);
        return metadata.ToolCapabilities.SupportsToolCalling;
    }

    /// <inheritdoc/>
    public async Task<string> GetToolCallFormatAsync(string modelId, CancellationToken cancellationToken = default)
    {
        var metadata = await GetModelMetadataAsync(modelId, cancellationToken);
        return metadata.ToolCapabilities.ToolCallFormat;
    }

    /// <summary>
    /// Convert LLamaEngine metadata to SDK metadata format
    /// </summary>
    private LMSupplyDepots.SDK.Services.ModelMetadata ConvertToSdkMetadata(
        LMSupplyDepots.External.LLamaEngine.Services.ModelMetadata llamaMetadata)
    {
        return new LMSupplyDepots.SDK.Services.ModelMetadata
        {
            Architecture = llamaMetadata.Architecture,
            ModelName = llamaMetadata.ModelName,
            ModelType = llamaMetadata.ModelType,
            ChatTemplate = llamaMetadata.ChatTemplate,
            SpecialTokens = llamaMetadata.SpecialTokens.ToDictionary(
                kvp => kvp.Key,
                kvp => (object)kvp.Value),
            ToolCapabilities = new LMSupplyDepots.SDK.Services.ToolCapabilities
            {
                SupportsToolCalling = llamaMetadata.ToolCapabilities.SupportsToolCalling,
                ToolCallFormat = llamaMetadata.ToolCapabilities.ToolCallFormat,
                ToolTokens = llamaMetadata.ToolCapabilities.ToolTokens,
                StartToken = llamaMetadata.ToolCapabilities.ToolTokens.TryGetValue("start", out var start) ? start : string.Empty,
                EndToken = llamaMetadata.ToolCapabilities.ToolTokens.TryGetValue("end", out var end) ? end : string.Empty
            },
            ContextLength = llamaMetadata.ContextLength,
            VocabularySize = llamaMetadata.VocabularySize,
            EmbeddingLength = llamaMetadata.EmbeddingLength,
            RawMetadata = llamaMetadata.RawMetadata
        };
    }
}
