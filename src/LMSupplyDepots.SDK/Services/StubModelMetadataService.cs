using LMSupplyDepots.SDK.Services;
using Microsoft.Extensions.Logging;

namespace LMSupplyDepots.SDK.Services;

/// <summary>
/// Stub implementation of IModelMetadataService for basic functionality
/// </summary>
public class StubModelMetadataService : IModelMetadataService
{
    private readonly ILogger<StubModelMetadataService> _logger;

    public StubModelMetadataService(ILogger<StubModelMetadataService> logger)
    {
        _logger = logger;
    }

    public Task<ModelMetadata> GetModelMetadataAsync(string modelId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting stub metadata for model {ModelId}", modelId);

        // Return basic metadata based on model name patterns
        var metadata = new ModelMetadata
        {
            Architecture = DetermineArchitecture(modelId),
            ModelName = modelId,
            ModelType = "chat",
            ContextLength = 4096,
            VocabularySize = 32000,
            ToolCapabilities = DetermineToolCapabilities(modelId),
            RawMetadata = new Dictionary<string, string>
            {
                ["model.id"] = modelId,
                ["stub"] = "true"
            }
        };

        return Task.FromResult(metadata);
    }

    public Task<string> ApplyChatTemplateAsync(
        string modelId, 
        IEnumerable<ChatMessage> messages, 
        bool addGenerationPrompt = true, 
        ToolCallOptions? toolOptions = null, 
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Applying stub chat template for model {ModelId}", modelId);

        // Simple concatenation fallback
        var result = string.Join("\n", messages.Select(m => $"{m.Role}: {m.Content}"));
        
        if (toolOptions?.HasTools == true)
        {
            var toolsInfo = string.Join(", ", toolOptions.Tools.Select(t => t.Name));
            result = $"Available tools: {toolsInfo}\n\n{result}";
        }

        if (addGenerationPrompt)
        {
            result += "\nassistant: ";
        }

        return Task.FromResult(result);
    }

    public Task<bool> SupportsToolCallingAsync(string modelId, CancellationToken cancellationToken = default)
    {
        // Assume most modern models support some form of tool calling
        return Task.FromResult(true);
    }

    public Task<string> GetToolCallFormatAsync(string modelId, CancellationToken cancellationToken = default)
    {
        var architecture = DetermineArchitecture(modelId);
        var format = architecture switch
        {
            "phi4" or "phi3" => "phi3",
            "llama" => "llama",
            "qwen" => "qwen",
            "mixtral" => "mixtral",
            _ => "generic"
        };

        return Task.FromResult(format);
    }

    private string DetermineArchitecture(string modelId)
    {
        var modelLower = modelId.ToLowerInvariant();

        if (modelLower.Contains("phi-4") || modelLower.Contains("phi4"))
            return "phi4";
        if (modelLower.Contains("phi-3") || modelLower.Contains("phi3") || modelLower.Contains("phi"))
            return "phi3";
        if (modelLower.Contains("llama"))
            return "llama";
        if (modelLower.Contains("qwen"))
            return "qwen";
        if (modelLower.Contains("mixtral") || modelLower.Contains("mistral"))
            return "mixtral";

        return "unknown";
    }

    private ToolCapabilities DetermineToolCapabilities(string modelId)
    {
        var architecture = DetermineArchitecture(modelId);
        var capabilities = new ToolCapabilities
        {
            SupportsToolCalling = true
        };

        switch (architecture)
        {
            case "phi4":
            case "phi3":
                capabilities.ToolCallFormat = "phi3";
                capabilities.ToolTokens = new Dictionary<string, string>
                {
                    ["start"] = "<|tool|>",
                    ["end"] = "<|end|>"
                };
                break;

            case "llama":
                capabilities.ToolCallFormat = "llama";
                capabilities.ToolTokens = new Dictionary<string, string>
                {
                    ["start"] = "[TOOL_CALL]",
                    ["end"] = "[/TOOL_CALL]"
                };
                break;

            case "qwen":
                capabilities.ToolCallFormat = "qwen";
                capabilities.ToolTokens = new Dictionary<string, string>
                {
                    ["start"] = "<function_calls>",
                    ["end"] = "</function_calls>"
                };
                break;

            case "mixtral":
                capabilities.ToolCallFormat = "mixtral";
                break;

            default:
                capabilities.ToolCallFormat = "generic";
                capabilities.SupportsToolCalling = false;
                break;
        }

        return capabilities;
    }
}