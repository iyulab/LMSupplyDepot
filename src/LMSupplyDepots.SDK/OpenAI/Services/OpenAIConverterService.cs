using LMSupplyDepots.SDK.OpenAI.Models;
using LMSupplyDepots.Models;
using LMSupplyDepots.Contracts;
using LMSupplyDepots.SDK.Services;
using LMSupplyDepots.External.LLamaEngine.Services;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace LMSupplyDepots.SDK.OpenAI.Services;

/// <summary>
/// Service for converting between OpenAI API format and internal LLama/model formats
/// </summary>
public interface IOpenAIConverterService
{
    /// <summary>
    /// Converts internal LMModel to OpenAI-compatible model
    /// </summary>
    OpenAIModel ConvertToOpenAIModel(LMModel model, long timestamp);

    /// <summary>
    /// Converts OpenAI chat completion request to internal generation request
    /// </summary>
    Task<GenerationRequest> ConvertToGenerationRequestAsync(
        OpenAIChatCompletionRequest request, 
        IModelMetadataService? metadataService = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts internal generation response to OpenAI chat completion response
    /// </summary>
    OpenAIChatCompletionResponse ConvertToOpenAIResponse(
        GenerationResponse response,
        string requestModel,
        string completionId,
        long timestamp);

    /// <summary>
    /// Converts OpenAI embedding request to internal embedding request
    /// </summary>
    EmbeddingRequest ConvertToEmbeddingRequest(OpenAIEmbeddingRequest request);

    /// <summary>
    /// Converts internal embedding response to OpenAI embedding response
    /// </summary>
    OpenAIEmbeddingResponse ConvertToOpenAIEmbeddingResponse(
        EmbeddingResponse response,
        string requestModel);

    /// <summary>
    /// Extracts text content from OpenAI messages for LLama processing (fallback method)
    /// </summary>
    string ConvertMessagesToPrompt(List<OpenAIChatMessage> messages);
}

/// <summary>
/// Implementation of OpenAI converter service
/// </summary>
public class OpenAIConverterService : IOpenAIConverterService
{
    private readonly ILogger<OpenAIConverterService> _logger;
    private readonly IStopTokenOptimizer? _stopTokenOptimizer;

    public OpenAIConverterService(ILogger<OpenAIConverterService> logger, IStopTokenOptimizer? stopTokenOptimizer = null)
    {
        _logger = logger;
        _stopTokenOptimizer = stopTokenOptimizer;
    }

    public OpenAIModel ConvertToOpenAIModel(LMModel model, long timestamp)
    {
        return new OpenAIModel
        {
            Id = model.Key, // Use Key (alias if available, otherwise Id)
            Created = timestamp,
            OwnedBy = "local",
            Type = ConvertModelType(model.Type)
        };
    }

    public async Task<GenerationRequest> ConvertToGenerationRequestAsync(
        OpenAIChatCompletionRequest request, 
        IModelMetadataService? metadataService = null,
        CancellationToken cancellationToken = default)
    {
        string prompt;
        string? modelArchitecture = null;

        // Check if any message contains tool calls for enhanced processing
        bool hasToolCalls = request.Messages.Any(m => m.ToolCalls != null && m.ToolCalls.Count > 0);

        // Try to use model's native chat template if metadata service is available
        if (metadataService != null)
        {
            try
            {
                // Use enhanced tool calling method if tools are present
                if (hasToolCalls)
                {
                    prompt = await ConvertMessagesToPromptWithToolsAsync(
                        request.Messages, 
                        request.Model, 
                        metadataService, 
                        cancellationToken);
                    
                    _logger.LogDebug("Successfully applied tool-aware chat template for model {Model}", request.Model);
                }
                else
                {
                    var chatMessages = request.Messages.Select(msg => new ChatMessage
                    {
                        Role = msg.Role.ToLowerInvariant(),
                        Content = ExtractTextContent(msg.Content)
                    }).ToList();

                    prompt = await metadataService.ApplyChatTemplateAsync(
                        request.Model,
                        chatMessages,
                        addGenerationPrompt: true,
                        toolOptions: null,
                        cancellationToken);
                    
                    _logger.LogDebug("Successfully applied native chat template for model {Model}", request.Model);
                }

                // Get model architecture for stop sequence filtering
                try
                {
                    var metadata = await metadataService.GetModelMetadataAsync(request.Model, cancellationToken);
                    modelArchitecture = metadata.Architecture;
                }
                catch
                {
                    // Ignore metadata errors, use fallback
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to apply native chat template for model {Model}, falling back to simple format", request.Model);
                prompt = hasToolCalls ? 
                    await ConvertMessagesToPromptWithToolsAsync(request.Messages, request.Model, null, cancellationToken) :
                    ConvertMessagesToPrompt(request.Messages);
            }
        }
        else
        {
            _logger.LogDebug("No metadata service available, using fallback prompt format for model {Model}", request.Model);
            prompt = hasToolCalls ? 
                await ConvertMessagesToPromptWithToolsAsync(request.Messages, request.Model, null, cancellationToken) :
                ConvertMessagesToPrompt(request.Messages);
        }

        var generationRequest = new GenerationRequest
        {
            Model = request.Model,
            Prompt = prompt,
            MaxTokens = request.MaxCompletionTokens ?? 256,
            Temperature = request.Temperature ?? 0.7f,
            TopP = request.TopP ?? 0.95f,
            Stream = request.Stream ?? false,
            Parameters = new Dictionary<string, object?>()
        };

        // Handle stop sequences with advanced optimization
        List<string> stopSequences = await OptimizeStopTokensAsync(request, modelArchitecture, metadataService, hasToolCalls, cancellationToken);
        
        // Apply stop sequences to generation request
        if (stopSequences.Count > 0)
        {
            generationRequest.Parameters["antiprompt"] = stopSequences;
            _logger.LogDebug("Added {Count} stop sequences to generation request: {StopSequences}", 
                stopSequences.Count, string.Join(", ", stopSequences));
        }

        if (request.PresencePenalty.HasValue)
        {
            generationRequest.Parameters["presence_penalty"] = request.PresencePenalty.Value;
        }

        if (request.FrequencyPenalty.HasValue)
        {
            generationRequest.Parameters["frequency_penalty"] = request.FrequencyPenalty.Value;
        }

        if (request.Seed.HasValue)
        {
            generationRequest.Parameters["seed"] = request.Seed.Value;
        }

        if (request.LogitBias != null && request.LogitBias.Count > 0)
        {
            generationRequest.Parameters["logit_bias"] = request.LogitBias;
        }

        return generationRequest;
    }

    /// <summary>
    /// Legacy synchronous method for backward compatibility
    /// </summary>
    public GenerationRequest ConvertToGenerationRequest(OpenAIChatCompletionRequest request)
    {
        return ConvertToGenerationRequestAsync(request).GetAwaiter().GetResult();
    }

    public OpenAIChatCompletionResponse ConvertToOpenAIResponse(
        GenerationResponse response,
        string requestModel,
        string completionId,
        long timestamp)
    {
        // Clean the response text to remove any conversation formatting artifacts
        var cleanedText = CleanResponseText(response.Text);

        return new OpenAIChatCompletionResponse
        {
            Id = completionId,
            Created = timestamp,
            Model = requestModel,
            Choices = new List<OpenAIChatChoice>
            {
                new()
                {
                    Index = 0,
                    Message = new OpenAIChatMessage
                    {
                        Role = "assistant",
                        Content = cleanedText
                    },
                    FinishReason = ConvertFinishReason(response.FinishReason)
                }
            },
            Usage = new OpenAIUsage
            {
                PromptTokens = response.PromptTokens,
                CompletionTokens = response.OutputTokens,
                TotalTokens = response.PromptTokens + response.OutputTokens
            }
        };
    }

    public EmbeddingRequest ConvertToEmbeddingRequest(OpenAIEmbeddingRequest request)
    {
        var texts = ConvertInputToTexts(request.Input);

        return new EmbeddingRequest
        {
            Model = request.Model,
            Texts = texts,
            Normalize = false
        };
    }

    public OpenAIEmbeddingResponse ConvertToOpenAIEmbeddingResponse(
        EmbeddingResponse response,
        string requestModel)
    {
        return new OpenAIEmbeddingResponse
        {
            Model = requestModel,
            Data = response.Embeddings.Select((embedding, index) => new OpenAIEmbeddingData
            {
                Index = index,
                Embedding = embedding
            }).ToList(),
            Usage = new OpenAIUsage
            {
                PromptTokens = response.TotalTokens,
                TotalTokens = response.TotalTokens
            }
        };
    }

    public string ConvertMessagesToPrompt(List<OpenAIChatMessage> messages)
    {
        var promptParts = new List<string>();

        foreach (var message in messages)
        {
            var content = ExtractTextContent(message.Content);

            switch (message.Role.ToLowerInvariant())
            {
                case "system":
                case "developer":
                    promptParts.Add($"System: {content}");
                    break;
                case "user":
                    promptParts.Add($"User: {content}");
                    break;
                case "assistant":
                    if (!string.IsNullOrEmpty(content))
                    {
                        promptParts.Add($"Assistant: {content}");
                    }
                    // Handle tool calls if present
                    if (message.ToolCalls != null && message.ToolCalls.Count > 0)
                    {
                        foreach (var toolCall in message.ToolCalls)
                        {
                            var functionCall = ExtractFunctionCall(toolCall);
                            promptParts.Add($"Assistant calls function: {functionCall}");
                        }
                    }
                    break;
                case "tool":
                    promptParts.Add($"Tool ({message.ToolCallId}): {content}");
                    break;
                default:
                    promptParts.Add($"{message.Role}: {content}");
                    break;
            }
        }

        return string.Join("\n\n", promptParts) + "\n\nAssistant:";
    }

    /// <summary>
    /// Convert messages to prompt with tool calling support using GGUF metadata
    /// </summary>
    public async Task<string> ConvertMessagesToPromptWithToolsAsync(
        List<OpenAIChatMessage> messages, 
        string modelId,
        IModelMetadataService? metadataService = null,
        CancellationToken cancellationToken = default)
    {
        // Try to get tool calling format from GGUF metadata first
        if (metadataService != null)
        {
            try
            {
                var metadata = await metadataService.GetModelMetadataAsync(modelId, cancellationToken);
                var toolCapabilities = metadata.ToolCapabilities;

                if (toolCapabilities.SupportsToolCalling)
                {
                    _logger.LogInformation("Using GGUF-derived tool calling format: {Format} with syntax: {Syntax}", 
                        toolCapabilities.ToolCallFormat, toolCapabilities.ToolCallSyntax);
                    
                    return FormatMessagesWithToolSupport(messages, toolCapabilities);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get tool calling format from GGUF metadata, using fallback");
            }
        }

        // Fallback to architecture-based tool calling
        var architecture = await GetModelArchitectureAsync(modelId, metadataService, cancellationToken);
        if (!string.IsNullOrEmpty(architecture))
        {
            var fallbackToolFormat = GetFallbackToolFormat(architecture);
            if (fallbackToolFormat.SupportsToolCalling)
            {
                _logger.LogInformation("Using fallback tool calling format for {Architecture}: {Format}", 
                    architecture, fallbackToolFormat.ToolCallFormat);
                
                return FormatMessagesWithToolSupport(messages, fallbackToolFormat);
            }
        }

        // No tool calling support, use standard prompt format
        _logger.LogDebug("No tool calling support detected, using standard prompt format");
        return ConvertMessagesToPrompt(messages);
    }

    /// <summary>
    /// Advanced stop token optimization using the dedicated optimizer service
    /// </summary>
    private async Task<List<string>> OptimizeStopTokensAsync(
        OpenAIChatCompletionRequest request,
        string? modelArchitecture,
        IModelMetadataService? metadataService,
        bool hasToolCalls,
        CancellationToken cancellationToken)
    {
        // Get request stop tokens
        var requestStopTokens = request.Stop != null ? ConvertStopSequence(request.Stop) : new List<string>();

        // Use advanced optimizer if available
        if (_stopTokenOptimizer != null && !string.IsNullOrEmpty(modelArchitecture))
        {
            var context = new ModelOptimizationContext
            {
                SupportsToolCalling = hasToolCalls,
                ChatTemplateFormat = GetChatTemplateFormat(modelArchitecture),
                HasSystemMessages = request.Messages.Any(m => m.Role.ToLowerInvariant() == "system"),
                HasToolResults = request.Messages.Any(m => m.Role.ToLowerInvariant() == "tool"),
                ExpectedLength = DetermineExpectedLength(request.MaxCompletionTokens ?? 256),
                Strategy = DetermineStopTokenStrategy(request.Temperature ?? 0.7f),
                MaxTokens = request.MaxCompletionTokens ?? 256,
                Temperature = request.Temperature ?? 0.7f
            };

            try
            {
                var optimizedStops = _stopTokenOptimizer.OptimizeStopTokens(modelArchitecture, requestStopTokens, context);

                _logger.LogInformation("Advanced stop token optimization for {Architecture}: {Count} total, {Primary} primary, {Secondary} secondary. Reasoning: {Reasoning}",
                    modelArchitecture, optimizedStops.GetAllStopTokens().Count,
                    optimizedStops.PrimaryStopTokens.Count, optimizedStops.SecondaryStopTokens.Count,
                    optimizedStops.OptimizationReasoning);

                return optimizedStops.GetAllStopTokens();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Advanced stop token optimization failed, falling back to legacy method");
            }
        }

        // Fallback to legacy stop token handling
        return await LegacyStopTokenHandlingAsync(requestStopTokens, modelArchitecture, metadataService, cancellationToken);
    }

    /// <summary>
    /// Legacy stop token handling for backward compatibility
    /// </summary>
    private async Task<List<string>> LegacyStopTokenHandlingAsync(
        List<string> requestStopTokens,
        string? modelArchitecture,
        IModelMetadataService? metadataService,
        CancellationToken cancellationToken)
    {
        var stopSequences = new List<string>(requestStopTokens);

        // Try to get stop tokens from model metadata first
        if (metadataService != null)
        {
            try
            {
                var metadata = await metadataService.GetModelMetadataAsync("model", cancellationToken);
                if (metadata.StopTokens.Any())
                {
                    stopSequences = metadata.StopTokens.ToList();
                    _logger.LogInformation("Using {Count} stop tokens from GGUF metadata", stopSequences.Count);
                }
                else if (stopSequences.Count == 0 && !string.IsNullOrEmpty(modelArchitecture))
                {
                    stopSequences = GetDefaultStopTokensForArchitecture(modelArchitecture);
                    _logger.LogInformation("Using architecture defaults for {Architecture}", modelArchitecture);
                }
                else if (stopSequences.Count > 0 && !string.IsNullOrEmpty(modelArchitecture))
                {
                    stopSequences = FilterStopSequencesForArchitecture(stopSequences, modelArchitecture);
                    if (stopSequences.Count == 0)
                    {
                        stopSequences = GetDefaultStopTokensForArchitecture(modelArchitecture);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get stop tokens from metadata");
                if (stopSequences.Count == 0 && !string.IsNullOrEmpty(modelArchitecture))
                {
                    stopSequences = GetDefaultStopTokensForArchitecture(modelArchitecture);
                }
            }
        }
        else if (stopSequences.Count == 0 && !string.IsNullOrEmpty(modelArchitecture))
        {
            stopSequences = GetDefaultStopTokensForArchitecture(modelArchitecture);
        }

        return stopSequences;
    }

    /// <summary>
    /// Determine chat template format from architecture
    /// </summary>
    private string GetChatTemplateFormat(string architecture)
    {
        return architecture.ToLowerInvariant() switch
        {
            "llama" => "llama-native",
            "phi3" or "phi" => "phi",
            "mistral" or "mixtral" => "mistral",
            "qwen" or "qwen2" => "chatml",
            "gemma" => "gemma",
            "deepseek" => "deepseek",
            _ => "generic"
        };
    }

    /// <summary>
    /// Determine expected generation length category
    /// </summary>
    private GenerationLength DetermineExpectedLength(int maxTokens)
    {
        return maxTokens switch
        {
            < 50 => GenerationLength.Short,
            < 200 => GenerationLength.Medium,
            < 1000 => GenerationLength.Long,
            _ => GenerationLength.VeryLong
        };
    }

    /// <summary>
    /// Determine stop token strategy based on temperature
    /// </summary>
    private StopTokenStrategy DetermineStopTokenStrategy(float temperature)
    {
        return temperature switch
        {
            < 0.3f => StopTokenStrategy.Conservative,
            < 1.0f => StopTokenStrategy.Balanced,
            _ => StopTokenStrategy.Permissive
        };
    }

    #region Private Helper Methods

    /// <summary>
    /// Format messages with tool calling support based on model capabilities
    /// </summary>
    private string FormatMessagesWithToolSupport(List<OpenAIChatMessage> messages, ToolCapabilities toolCapabilities)
    {
        var format = toolCapabilities.ToolCallFormat.ToLowerInvariant();
        var syntax = toolCapabilities.ToolCallSyntax.ToLowerInvariant();
        
        // Validate tool tokens don't conflict with stop sequences
        var validatedTokens = ValidateToolTokensForCompatibility(toolCapabilities.ToolTokens, format);
        var validatedCapabilities = new ToolCapabilities
        {
            SupportsToolCalling = toolCapabilities.SupportsToolCalling,
            ToolCallFormat = toolCapabilities.ToolCallFormat,
            ToolCallSyntax = toolCapabilities.ToolCallSyntax,
            ToolTokens = validatedTokens
        };
        
        return format switch
        {
            "llama-native" => FormatLlamaNativeToolCalls(messages, validatedCapabilities.ToolTokens),
            "phi" or "phi3" or "phi3.5" or "phi4" => FormatPhiToolCalls(messages, validatedCapabilities.ToolTokens),
            "chatml" => FormatChatMLToolCalls(messages, validatedCapabilities.ToolTokens),
            "mistral" => FormatMistralToolCalls(messages, validatedCapabilities.ToolTokens),
            "deepseek" => FormatDeepSeekToolCalls(messages, validatedCapabilities.ToolTokens),
            "gemma" => FormatGemmaToolCalls(messages, validatedCapabilities.ToolTokens),
            "function" => FormatFunctionToolCalls(messages, validatedCapabilities.ToolTokens),
            _ => FormatGenericToolCalls(messages, syntax, validatedCapabilities.ToolTokens)
        };
    }

    /// <summary>
    /// Get model architecture for fallback tool calling format detection
    /// </summary>
    private async Task<string?> GetModelArchitectureAsync(string modelId, IModelMetadataService? metadataService, CancellationToken cancellationToken)
    {
        if (metadataService == null) return null;
        
        try
        {
            var metadata = await metadataService.GetModelMetadataAsync(modelId, cancellationToken);
            return metadata.Architecture;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Get fallback tool calling format based on architecture
    /// </summary>
    private ToolCapabilities GetFallbackToolFormat(string architecture)
    {
        return architecture.ToLowerInvariant() switch
        {
            "llama" => new ToolCapabilities 
            { 
                SupportsToolCalling = true, 
                ToolCallFormat = "llama-native", 
                ToolCallSyntax = "json",
                ToolTokens = new Dictionary<string, string>
                {
                    ["tool_call_start"] = "<|start_header_id|>assistant<|end_header_id|>",
                    ["tool_call_end"] = "<|eot_id|>",
                    ["function_start"] = "<|python_tag|>",
                    ["function_end"] = "<|eot_id|>"
                }
            },
            "phi3" or "phi" => new ToolCapabilities 
            { 
                SupportsToolCalling = true, 
                ToolCallFormat = "phi", 
                ToolCallSyntax = "xml",
                ToolTokens = new Dictionary<string, string>
                {
                    ["tool_start"] = "<|tool|>",
                    ["tool_end"] = "<|/tool|>",
                    ["call_start"] = "<|tool_call|>",
                    ["call_end"] = "<|/tool_call|>"
                }
            },
            "qwen" or "qwen2" => new ToolCapabilities 
            { 
                SupportsToolCalling = true, 
                ToolCallFormat = "chatml", 
                ToolCallSyntax = "json",
                ToolTokens = new Dictionary<string, string>
                {
                    ["tool_start"] = "<|im_start|>tool",
                    ["tool_end"] = "<|im_end|>",
                    ["function_start"] = "<|im_start|>function",
                    ["function_end"] = "<|im_end|>"
                }
            },
            "mistral" or "mixtral" => new ToolCapabilities 
            { 
                SupportsToolCalling = true, 
                ToolCallFormat = "mistral", 
                ToolCallSyntax = "json",
                ToolTokens = new Dictionary<string, string>
                {
                    ["tool_start"] = "[TOOL_CALLS]",
                    ["tool_end"] = "[/TOOL_CALLS]",
                    ["function_start"] = "{",
                    ["function_end"] = "}"
                }
            },
            "deepseek" => new ToolCapabilities 
            { 
                SupportsToolCalling = true, 
                ToolCallFormat = "deepseek", 
                ToolCallSyntax = "markdown",
                ToolTokens = new Dictionary<string, string>
                {
                    ["tool_start"] = "```tool_call",
                    ["tool_end"] = "```",
                    ["function_start"] = "{",
                    ["function_end"] = "}"
                }
            },
            "gemma" => new ToolCapabilities 
            { 
                SupportsToolCalling = true, 
                ToolCallFormat = "gemma", 
                ToolCallSyntax = "json",
                ToolTokens = new Dictionary<string, string>
                {
                    ["tool_start"] = "<start_of_turn>tool",
                    ["tool_end"] = "<end_of_turn>",
                    ["function_start"] = "{",
                    ["function_end"] = "}"
                }
            },
            _ => new ToolCapabilities { SupportsToolCalling = false }
        };
    }

    /// <summary>
    /// Format LLaMA native tool calls
    /// </summary>
    private string FormatLlamaNativeToolCalls(List<OpenAIChatMessage> messages, Dictionary<string, string> toolTokens)
    {
        var promptParts = new List<string>();
        var toolCallStart = toolTokens.GetValueOrDefault("tool_call_start", "<|start_header_id|>assistant<|end_header_id|>");
        var toolCallEnd = toolTokens.GetValueOrDefault("tool_call_end", "<|eot_id|>");
        var functionStart = toolTokens.GetValueOrDefault("function_start", "<|python_tag|>");
        var functionEnd = toolTokens.GetValueOrDefault("function_end", "<|eot_id|>");

        foreach (var message in messages)
        {
            var content = ExtractTextContent(message.Content);

            switch (message.Role.ToLowerInvariant())
            {
                case "system":
                    promptParts.Add($"<|start_header_id|>system<|end_header_id|>\n\n{content}<|eot_id|>");
                    break;
                case "user":
                    promptParts.Add($"<|start_header_id|>user<|end_header_id|>\n\n{content}<|eot_id|>");
                    break;
                case "assistant":
                    if (!string.IsNullOrEmpty(content))
                    {
                        promptParts.Add($"<|start_header_id|>assistant<|end_header_id|>\n\n{content}<|eot_id|>");
                    }
                    if (message.ToolCalls != null && message.ToolCalls.Count > 0)
                    {
                        foreach (var toolCall in message.ToolCalls)
                        {
                            var functionCall = JsonSerializer.Serialize(new { 
                                name = toolCall.Function?.Name, 
                                arguments = toolCall.Function?.Arguments 
                            });
                            promptParts.Add($"{toolCallStart}\n\n{functionStart}\n{functionCall}\n{functionEnd}");
                        }
                    }
                    break;
                case "tool":
                    promptParts.Add($"<|start_header_id|>tool<|end_header_id|>\n\n{content}<|eot_id|>");
                    break;
            }
        }

        return string.Join("\n", promptParts) + "\n<|start_header_id|>assistant<|end_header_id|>\n\n";
    }

    /// <summary>
    /// Format Phi tool calls
    /// </summary>
    private string FormatPhiToolCalls(List<OpenAIChatMessage> messages, Dictionary<string, string> toolTokens)
    {
        var promptParts = new List<string>();
        var toolStart = toolTokens.GetValueOrDefault("tool_start", "<|tool|>");
        var toolEnd = toolTokens.GetValueOrDefault("tool_end", "<|/tool|>");

        foreach (var message in messages)
        {
            var content = ExtractTextContent(message.Content);

            switch (message.Role.ToLowerInvariant())
            {
                case "system":
                    promptParts.Add($"<|system|>\n{content}<|end|>");
                    break;
                case "user":
                    promptParts.Add($"<|user|>\n{content}<|end|>");
                    break;
                case "assistant":
                    if (!string.IsNullOrEmpty(content))
                    {
                        promptParts.Add($"<|assistant|>\n{content}<|end|>");
                    }
                    if (message.ToolCalls != null && message.ToolCalls.Count > 0)
                    {
                        foreach (var toolCall in message.ToolCalls)
                        {
                            var toolXml = $"<tool_call>\n<name>{toolCall.Function?.Name}</name>\n<arguments>{toolCall.Function?.Arguments}</arguments>\n</tool_call>";
                            promptParts.Add($"<|assistant|>\n{toolStart}\n{toolXml}\n{toolEnd}<|end|>");
                        }
                    }
                    break;
                case "tool":
                    promptParts.Add($"<|tool|>\n{content}<|end|>");
                    break;
            }
        }

        return string.Join("\n", promptParts) + "\n<|assistant|>\n";
    }

    /// <summary>
    /// Format ChatML tool calls
    /// </summary>
    private string FormatChatMLToolCalls(List<OpenAIChatMessage> messages, Dictionary<string, string> toolTokens)
    {
        var promptParts = new List<string>();
        var toolStart = toolTokens.GetValueOrDefault("tool_start", "<|im_start|>tool");
        var toolEnd = toolTokens.GetValueOrDefault("tool_end", "<|im_end|>");

        foreach (var message in messages)
        {
            var content = ExtractTextContent(message.Content);

            switch (message.Role.ToLowerInvariant())
            {
                case "system":
                    promptParts.Add($"<|im_start|>system\n{content}<|im_end|>");
                    break;
                case "user":
                    promptParts.Add($"<|im_start|>user\n{content}<|im_end|>");
                    break;
                case "assistant":
                    if (!string.IsNullOrEmpty(content))
                    {
                        promptParts.Add($"<|im_start|>assistant\n{content}<|im_end|>");
                    }
                    if (message.ToolCalls != null && message.ToolCalls.Count > 0)
                    {
                        foreach (var toolCall in message.ToolCalls)
                        {
                            var functionCall = JsonSerializer.Serialize(new { 
                                name = toolCall.Function?.Name, 
                                arguments = toolCall.Function?.Arguments 
                            });
                            promptParts.Add($"{toolStart}\n{functionCall}\n{toolEnd}");
                        }
                    }
                    break;
                case "tool":
                    promptParts.Add($"<|im_start|>tool\n{content}<|im_end|>");
                    break;
            }
        }

        return string.Join("\n", promptParts) + "\n<|im_start|>assistant\n";
    }

    /// <summary>
    /// Format Mistral tool calls
    /// </summary>
    private string FormatMistralToolCalls(List<OpenAIChatMessage> messages, Dictionary<string, string> toolTokens)
    {
        var promptParts = new List<string>();
        var toolStart = toolTokens.GetValueOrDefault("tool_start", "[TOOL_CALLS]");
        var toolEnd = toolTokens.GetValueOrDefault("tool_end", "[/TOOL_CALLS]");

        foreach (var message in messages)
        {
            var content = ExtractTextContent(message.Content);

            switch (message.Role.ToLowerInvariant())
            {
                case "system":
                    promptParts.Add($"<<SYS>>\n{content}\n<</SYS>>");
                    break;
                case "user":
                    promptParts.Add($"[INST] {content} [/INST]");
                    break;
                case "assistant":
                    if (!string.IsNullOrEmpty(content))
                    {
                        promptParts.Add(content);
                    }
                    if (message.ToolCalls != null && message.ToolCalls.Count > 0)
                    {
                        var toolCalls = message.ToolCalls.Select(tc => new { 
                            name = tc.Function?.Name, 
                            arguments = tc.Function?.Arguments 
                        });
                        var toolCallsJson = JsonSerializer.Serialize(toolCalls);
                        promptParts.Add($"{toolStart}\n{toolCallsJson}\n{toolEnd}");
                    }
                    break;
                case "tool":
                    promptParts.Add($"[TOOL_RESULT] {content} [/TOOL_RESULT]");
                    break;
            }
        }

        return string.Join("\n", promptParts) + "\n";
    }

    /// <summary>
    /// Format DeepSeek tool calls
    /// </summary>
    private string FormatDeepSeekToolCalls(List<OpenAIChatMessage> messages, Dictionary<string, string> toolTokens)
    {
        var promptParts = new List<string>();
        var toolStart = toolTokens.GetValueOrDefault("tool_start", "```tool_call");
        var toolEnd = toolTokens.GetValueOrDefault("tool_end", "```");

        foreach (var message in messages)
        {
            var content = ExtractTextContent(message.Content);

            switch (message.Role.ToLowerInvariant())
            {
                case "system":
                    promptParts.Add($"System: {content}");
                    break;
                case "user":
                    promptParts.Add($"User: {content}");
                    break;
                case "assistant":
                    if (!string.IsNullOrEmpty(content))
                    {
                        promptParts.Add($"Assistant: {content}");
                    }
                    if (message.ToolCalls != null && message.ToolCalls.Count > 0)
                    {
                        foreach (var toolCall in message.ToolCalls)
                        {
                            var functionCall = JsonSerializer.Serialize(new { 
                                name = toolCall.Function?.Name, 
                                arguments = toolCall.Function?.Arguments 
                            });
                            promptParts.Add($"Assistant: {toolStart}\n{functionCall}\n{toolEnd}");
                        }
                    }
                    break;
                case "tool":
                    promptParts.Add($"Tool: {content}");
                    break;
            }
        }

        return string.Join("\n\n", promptParts) + "\n\nAssistant: ";
    }

    /// <summary>
    /// Format Gemma tool calls
    /// </summary>
    private string FormatGemmaToolCalls(List<OpenAIChatMessage> messages, Dictionary<string, string> toolTokens)
    {
        var promptParts = new List<string>();
        var toolStart = toolTokens.GetValueOrDefault("tool_start", "<start_of_turn>tool");
        var toolEnd = toolTokens.GetValueOrDefault("tool_end", "<end_of_turn>");

        foreach (var message in messages)
        {
            var content = ExtractTextContent(message.Content);

            switch (message.Role.ToLowerInvariant())
            {
                case "system":
                    promptParts.Add($"<start_of_turn>system\n{content}<end_of_turn>");
                    break;
                case "user":
                    promptParts.Add($"<start_of_turn>user\n{content}<end_of_turn>");
                    break;
                case "assistant":
                    if (!string.IsNullOrEmpty(content))
                    {
                        promptParts.Add($"<start_of_turn>model\n{content}<end_of_turn>");
                    }
                    if (message.ToolCalls != null && message.ToolCalls.Count > 0)
                    {
                        foreach (var toolCall in message.ToolCalls)
                        {
                            var functionCall = JsonSerializer.Serialize(new { 
                                name = toolCall.Function?.Name, 
                                arguments = toolCall.Function?.Arguments 
                            });
                            promptParts.Add($"{toolStart}\n{functionCall}\n{toolEnd}");
                        }
                    }
                    break;
                case "tool":
                    promptParts.Add($"<start_of_turn>tool\n{content}<end_of_turn>");
                    break;
            }
        }

        return string.Join("\n", promptParts) + "\n<start_of_turn>model\n";
    }

    /// <summary>
    /// Format function tool calls
    /// </summary>
    private string FormatFunctionToolCalls(List<OpenAIChatMessage> messages, Dictionary<string, string> toolTokens)
    {
        var promptParts = new List<string>();
        var functionStart = toolTokens.GetValueOrDefault("function_start", "function_call:");
        var functionEnd = toolTokens.GetValueOrDefault("function_end", "\n");

        foreach (var message in messages)
        {
            var content = ExtractTextContent(message.Content);

            switch (message.Role.ToLowerInvariant())
            {
                case "system":
                    promptParts.Add($"System: {content}");
                    break;
                case "user":
                    promptParts.Add($"User: {content}");
                    break;
                case "assistant":
                    if (!string.IsNullOrEmpty(content))
                    {
                        promptParts.Add($"Assistant: {content}");
                    }
                    if (message.ToolCalls != null && message.ToolCalls.Count > 0)
                    {
                        foreach (var toolCall in message.ToolCalls)
                        {
                            var functionCall = $"{toolCall.Function?.Name}({toolCall.Function?.Arguments})";
                            promptParts.Add($"Assistant: {functionStart} {functionCall}{functionEnd}");
                        }
                    }
                    break;
                case "tool":
                    promptParts.Add($"Tool: {content}");
                    break;
            }
        }

        return string.Join("\n", promptParts) + "\nAssistant: ";
    }

    /// <summary>
    /// Format generic tool calls with syntax awareness
    /// </summary>
    private string FormatGenericToolCalls(List<OpenAIChatMessage> messages, string syntax, Dictionary<string, string> toolTokens)
    {
        var promptParts = new List<string>();

        foreach (var message in messages)
        {
            var content = ExtractTextContent(message.Content);

            switch (message.Role.ToLowerInvariant())
            {
                case "system":
                    promptParts.Add($"System: {content}");
                    break;
                case "user":
                    promptParts.Add($"User: {content}");
                    break;
                case "assistant":
                    if (!string.IsNullOrEmpty(content))
                    {
                        promptParts.Add($"Assistant: {content}");
                    }
                    if (message.ToolCalls != null && message.ToolCalls.Count > 0)
                    {
                        foreach (var toolCall in message.ToolCalls)
                        {
                            var formattedCall = syntax switch
                            {
                                "json" => JsonSerializer.Serialize(new { 
                                    name = toolCall.Function?.Name, 
                                    arguments = toolCall.Function?.Arguments 
                                }),
                                "xml" => $"<tool_call><name>{toolCall.Function?.Name}</name><arguments>{toolCall.Function?.Arguments}</arguments></tool_call>",
                                "markdown" => $"```tool_call\n{{\n  \"name\": \"{toolCall.Function?.Name}\",\n  \"arguments\": {toolCall.Function?.Arguments}\n}}\n```",
                                _ => $"{toolCall.Function?.Name}({toolCall.Function?.Arguments})"
                            };
                            promptParts.Add($"Assistant: {formattedCall}");
                        }
                    }
                    break;
                case "tool":
                    promptParts.Add($"Tool: {content}");
                    break;
            }
        }

        return string.Join("\n\n", promptParts) + "\n\nAssistant: ";
    }

    /// <summary>
    /// Validate tool tokens for compatibility with chat template stop sequences
    /// </summary>
    private Dictionary<string, string> ValidateToolTokensForCompatibility(Dictionary<string, string> toolTokens, string format)
    {
        var validatedTokens = new Dictionary<string, string>(toolTokens);
        var problematicTokens = new List<string>();

        // Define known stop tokens by architecture to check for conflicts
        var stopTokensByArchitecture = new Dictionary<string, List<string>>
        {
            ["llama-native"] = new() { "<|eot_id|>", "<|start_header_id|>", "<|end_header_id|>" },
            ["phi"] = new() { "<|end|>", "<|assistant|>", "<|user|>", "<|system|>" },
            ["chatml"] = new() { "<|im_start|>", "<|im_end|>" },
            ["mistral"] = new() { "</s>", "[INST]", "[/INST]", "<<SYS>>", "<</SYS>>" },
            ["deepseek"] = new() { "User:", "Assistant:", "System:" },
            ["gemma"] = new() { "<start_of_turn>", "<end_of_turn>" }
        };

        if (stopTokensByArchitecture.TryGetValue(format, out var stopTokens))
        {
            foreach (var (key, token) in toolTokens)
            {
                foreach (var stopToken in stopTokens)
                {
                    // Check if tool token conflicts with stop token
                    if (token.Contains(stopToken) || stopToken.Contains(token))
                    {
                        _logger.LogWarning(
                            "Tool token '{Key}' value '{Token}' conflicts with stop token '{StopToken}' for format '{Format}', adjusting",
                            key, token, stopToken, format);

                        // Apply format-specific conflict resolution
                        var adjustedToken = ResolveToolTokenConflict(key, token, stopToken, format);
                        if (adjustedToken != token)
                        {
                            validatedTokens[key] = adjustedToken;
                            _logger.LogInformation(
                                "Adjusted tool token '{Key}' from '{OriginalToken}' to '{AdjustedToken}'",
                                key, token, adjustedToken);
                        }
                        else
                        {
                            problematicTokens.Add(key);
                        }
                    }
                }
            }
        }

        // Remove problematic tokens that couldn't be resolved
        foreach (var problemKey in problematicTokens)
        {
            validatedTokens.Remove(problemKey);
            _logger.LogWarning("Removed problematic tool token '{Key}' that conflicts with chat template", problemKey);
        }

        return validatedTokens;
    }

    /// <summary>
    /// Resolve tool token conflicts with chat template stop sequences
    /// </summary>
    private string ResolveToolTokenConflict(string tokenKey, string originalToken, string conflictingStopToken, string format)
    {
        // Format-specific conflict resolution strategies
        return format switch
        {
            "llama-native" => tokenKey switch
            {
                "tool_call_start" when originalToken.Contains("<|eot_id|>") => originalToken.Replace("<|eot_id|>", ""),
                "function_start" when originalToken == "<|python_tag|>" => "<|function_call|>", // Alternative that doesn't conflict
                _ => originalToken
            },
            "phi" => tokenKey switch
            {
                "tool_start" when originalToken.Contains("<|end|>") => originalToken.Replace("<|end|>", ""),
                "call_start" when originalToken.Contains("<|end|>") => originalToken.Replace("<|end|>", ""),
                _ => originalToken
            },
            "chatml" => tokenKey switch
            {
                "tool_start" when originalToken.Contains("<|im_end|>") => originalToken.Replace("<|im_end|>", ""),
                "function_start" when originalToken.Contains("<|im_end|>") => originalToken.Replace("<|im_end|>", ""),
                _ => originalToken
            },
            "mistral" => tokenKey switch
            {
                "tool_start" when originalToken.Contains("</s>") => originalToken.Replace("</s>", ""),
                "function_start" when originalToken.Contains("[INST]") => "{", // Use simple bracket
                _ => originalToken
            },
            "deepseek" => tokenKey switch
            {
                "tool_start" when originalToken.Contains("Assistant:") => "```function_call",
                "function_start" when originalToken.Contains("User:") => "{",
                _ => originalToken
            },
            "gemma" => tokenKey switch
            {
                "tool_start" when originalToken.Contains("<end_of_turn>") => originalToken.Replace("<end_of_turn>", ""),
                "function_start" when originalToken.Contains("<start_of_turn>") => "{",
                _ => originalToken
            },
            _ => originalToken
        };
    }

    /// <summary>
    /// Cleans response text to remove conversation formatting artifacts
    /// </summary>
    private string CleanResponseText(string responseText)
    {
        if (string.IsNullOrEmpty(responseText))
            return string.Empty;

        var cleaned = responseText.Trim();

        // Remove common conversation prefixes that models sometimes include
        var prefixesToRemove = new[]
        {
            "Assistant:",
            "ASSISTANT:",
            "AI:",
            "Bot:",
            "Response:",
            "Answer:"
        };

        foreach (var prefix in prefixesToRemove)
        {
            if (cleaned.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned.Substring(prefix.Length).TrimStart();
                break;
            }
        }

        // Remove trailing conversation-like patterns
        var linesToRemove = new[]
        {
            "User:",
            "USER:",
            "Human:",
            "HUMAN:",
            "\nUser:",
            "\nUSER:",
            "\nHuman:",
            "\nHUMAN:"
        };

        foreach (var pattern in linesToRemove)
        {
            var index = cleaned.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                cleaned = cleaned.Substring(0, index).TrimEnd();
                break;
            }
        }

        // Remove excessive newlines
        while (cleaned.Contains("\n\n\n"))
        {
            cleaned = cleaned.Replace("\n\n\n", "\n\n");
        }

        return cleaned.Trim();
    }

    private string ConvertModelType(ModelType modelType)
    {
        return modelType switch
        {
            ModelType.TextGeneration => "text-generation",
            ModelType.Embedding => "embedding",
            _ => "unknown"
        };
    }

    private List<string> ConvertStopSequence(StopSequence stopSequence)
    {
        var sequences = new List<string>();

        if (stopSequence.Single != null)
        {
            sequences.Add(stopSequence.Single);
        }
        else if (stopSequence.Multiple != null)
        {
            sequences.AddRange(stopSequence.Multiple);
        }

        return sequences;
    }

    private string? ConvertFinishReason(string finishReason)
    {
        return finishReason.ToLowerInvariant() switch
        {
            "length" => "length",
            "stop" => "stop",
            "eos" => "stop",
            "end_of_sequence" => "stop",
            "end_of_text" => "stop",
            "tool_calls" => "tool_calls",
            "function_call" => "function_call", // Deprecated but still supported
            "content_filter" => "content_filter",
            "filtered" => "content_filter",
            "safety" => "content_filter",
            "max_tokens" => "length",
            "max_length" => "length",
            "token_limit" => "length",
            "cancelled" => "stop",
            "canceled" => "stop",
            "" => "stop",
            null => "stop",
            _ => "stop" // Default fallback for unknown reasons
        };
    }

    private List<string> ConvertInputToTexts(object input)
    {
        var texts = new List<string>();

        if (input is string singleText)
        {
            texts.Add(singleText);
        }
        else if (input is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.String)
            {
                texts.Add(jsonElement.GetString() ?? string.Empty);
            }
            else if (jsonElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in jsonElement.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        texts.Add(item.GetString() ?? string.Empty);
                    }
                }
            }
        }
        else if (input is IEnumerable<string> stringArray)
        {
            texts.AddRange(stringArray);
        }

        return texts;
    }

    private string ExtractTextContent(ContentPart? content)
    {
        if (content == null) return string.Empty;

        return content switch
        {
            TextContentPart textPart => textPart.Text,
            _ => content.ToString() ?? string.Empty
        };
    }

    private string ExtractFunctionCall(ToolCall toolCall)
    {
        if (toolCall?.Function == null) return string.Empty;

        return $"{toolCall.Function.Name}({toolCall.Function.Arguments})";
    }

    /// <summary>
    /// Filters stop sequences to prevent conflicts with chat template tokens
    /// </summary>
    private List<string> FilterStopSequencesForArchitecture(List<string> stopSequences, string architecture)
    {
        var architectureTokens = GetArchitectureTokens(architecture);
        var filtered = new List<string>();

        foreach (var stop in stopSequences)
        {
            bool conflicts = false;
            
            // Check if this stop sequence conflicts with template tokens
            foreach (var templateToken in architectureTokens)
            {
                if (templateToken.Contains(stop) || stop.Contains(templateToken))
                {
                    _logger.LogWarning(
                        "Stop sequence '{StopSequence}' conflicts with template token '{TemplateToken}' for architecture '{Architecture}', filtering out", 
                        stop, templateToken, architecture);
                    conflicts = true;
                    break;
                }
            }

            // Special handling for newlines in Phi models - they are structural parts of the template
            if (architecture == "phi3" && IsNewlineStop(stop))
            {
                _logger.LogWarning(
                    "Stop sequence '{StopSequence}' is a newline which conflicts with Phi template structure, filtering out", 
                    stop);
                conflicts = true;
            }

            if (!conflicts)
            {
                filtered.Add(stop);
                _logger.LogDebug("Keeping stop sequence '{StopSequence}' for architecture '{Architecture}'", stop, architecture);
            }
        }

        return filtered;
    }

    /// <summary>
    /// Gets template tokens for specific architecture
    /// </summary>
    private List<string> GetArchitectureTokens(string architecture)
    {
        return architecture switch
        {
            "phi3" => new List<string> { "<|end|>", "<|assistant|>", "<|user|>", "<|system|>" },
            "llama" => new List<string> { 
                "</s>", "[INST]", "[/INST]", "<<SYS>>", "<</SYS>>",
                // LLaMA-3.2 specific tokens
                "<|start_header_id|>", "<|end_header_id|>", "<|eot_id|>",
                "user:", "assistant:", "system:"
            },
            "mistral" or "mixtral" => new List<string> { "</s>", "[INST]", "[/INST]" },
            "qwen" => new List<string> { "<|im_end|>", "<|im_start|>" },
            _ => new List<string>()
        };
    }

    /// <summary>
    /// Checks if a stop sequence is a newline variant
    /// </summary>
    private bool IsNewlineStop(string stop)
    {
        return stop == "\n" || stop == "\\n" || stop == "\r\n" || stop == "\\r\\n";
    }

    /// <summary>
    /// Gets appropriate default stop tokens for specific architecture
    /// </summary>
    private List<string> GetDefaultStopTokensForArchitecture(string architecture)
    {
        return architecture switch
        {
            "phi3" => new List<string> { "<|end|>" },
            "llama" => new List<string> { 
                "<|eot_id|>", // Primary LLaMA-3.2 stop token
                "</s>",       // General EOS token
                "<|start_header_id|>user<|end_header_id|>", // Prevent continuing to next user message
                "\nuser:",    // Additional safety stop
                "\nassistant:" // Prevent role confusion
            },
            "mistral" or "mixtral" => new List<string> { "</s>" },
            "qwen" => new List<string> { "<|im_end|>" },
            _ => new List<string>()
        };
    }

    #endregion
}
