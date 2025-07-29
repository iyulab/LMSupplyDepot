using LMSupplyDepots.SDK.OpenAI.Models;
using LMSupplyDepots.Models;
using LMSupplyDepots.Contracts;
using LMSupplyDepots.SDK.Services;
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

    public OpenAIConverterService(ILogger<OpenAIConverterService> logger)
    {
        _logger = logger;
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

        // Try to use model's native chat template if metadata service is available
        if (metadataService != null)
        {
            try
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
                    toolOptions: null, // TODO: Add tool support
                    cancellationToken);

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

                _logger.LogDebug("Successfully applied native chat template for model {Model}", request.Model);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to apply native chat template for model {Model}, falling back to simple format", request.Model);
                prompt = ConvertMessagesToPrompt(request.Messages);
            }
        }
        else
        {
            _logger.LogDebug("No metadata service available, using fallback prompt format for model {Model}", request.Model);
            prompt = ConvertMessagesToPrompt(request.Messages);
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

        // Add optional parameters
        if (request.Stop != null)
        {
            var stopSequences = ConvertStopSequence(request.Stop);
            
            // Filter stop sequences to prevent conflicts with chat templates
            if (!string.IsNullOrEmpty(modelArchitecture))
            {
                stopSequences = FilterStopSequencesForArchitecture(stopSequences, modelArchitecture);
                
                // Add architecture-specific default stop tokens if none remain
                if (stopSequences.Count == 0)
                {
                    stopSequences = GetDefaultStopTokensForArchitecture(modelArchitecture);
                    _logger.LogInformation("No valid stop sequences remain after filtering, using architecture defaults for {Architecture}: {StopTokens}", 
                        modelArchitecture, string.Join(", ", stopSequences));
                }
            }
            
            if (stopSequences.Count > 0)
            {
                generationRequest.Parameters["antiprompt"] = stopSequences;
            }
            else
            {
                _logger.LogInformation("All stop sequences were filtered out due to conflicts with model architecture {Architecture}", modelArchitecture);
            }
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

    #region Private Helper Methods

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
            "llama" => new List<string> { "</s>", "[INST]", "[/INST]", "<<SYS>>", "<</SYS>>" },
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
            "llama" => new List<string> { "</s>" },
            "mistral" or "mixtral" => new List<string> { "</s>" },
            "qwen" => new List<string> { "<|im_end|>" },
            _ => new List<string>()
        };
    }

    #endregion
}
