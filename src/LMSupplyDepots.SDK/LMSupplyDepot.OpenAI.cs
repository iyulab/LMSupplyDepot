using LMSupplyDepots.SDK.OpenAI.Models;
using LMSupplyDepots.SDK.OpenAI.Services;
using LMSupplyDepots.SDK.Services;
using LMSupplyDepots.SDK.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LMSupplyDepots.SDK;

/// <summary>
/// OpenAI compatibility functionality for LMSupplyDepot
/// </summary>
public partial class LMSupplyDepot
{
    /// <summary>
    /// Gets the OpenAI converter service
    /// </summary>
    private IOpenAIConverterService OpenAIConverter => _serviceProvider.GetRequiredService<IOpenAIConverterService>();

    /// <summary>
    /// Gets the tool service
    /// </summary>
    private IToolService ToolService => _serviceProvider.GetRequiredService<IToolService>();

    /// <summary>
    /// Gets the model metadata service
    /// </summary>
    private IModelMetadataService? ModelMetadataService => _serviceProvider.GetService<IModelMetadataService>();

    #region OpenAI Compatible Methods

    /// <summary>
    /// Lists available models in OpenAI-compatible format (only loaded models)
    /// </summary>
    public async Task<OpenAIModelsResponse> ListModelsOpenAIAsync(CancellationToken cancellationToken = default)
    {
        var models = await GetLoadedModelsAsync(cancellationToken);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var openAIModels = models.Select(model => OpenAIConverter.ConvertToOpenAIModel(model, timestamp)).ToList();

        return new OpenAIModelsResponse { Data = openAIModels };
    }

    /// <summary>
    /// Creates a chat completion (OpenAI-compatible)
    /// </summary>
    public async Task<OpenAIChatCompletionResponse> CreateChatCompletionAsync(
        OpenAIChatCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        // Validate request
        ValidateChatCompletionRequest(request);

        // Check if the model exists and is loaded
        var model = await GetModelAsync(request.Model, cancellationToken);
        if (model == null)
        {
            throw new ArgumentException($"Model '{request.Model}' not found", nameof(request));
        }

        var isLoaded = await IsModelLoadedAsync(model.Id, cancellationToken);
        if (!isLoaded)
        {
            throw new InvalidOperationException($"Model '{request.Model}' is not loaded");
        }

        // Check if the model supports text generation
        if (!model.Capabilities.SupportsTextGeneration)
        {
            throw new InvalidOperationException($"Model '{request.Model}' does not support text generation");
        }

        // Check for tool calls in the conversation
        var hasToolCalls = request.Messages.Any(m =>
            m.Role.ToLowerInvariant() == "assistant" &&
            m.ToolCalls != null &&
            m.ToolCalls.Count > 0);

        var hasToolResponses = request.Messages.Any(m => m.Role.ToLowerInvariant() == "tool");

        // If request has tools defined, apply model-specific tool formatting
        if (request.Tools != null && request.Tools.Count > 0)
        {
            await ApplyModelSpecificToolFormattingAsync(request, model.Id, cancellationToken);
        }

        // Convert OpenAI request to internal format
        var generationRequest = OpenAIConverter.ConvertToGenerationRequest(request);

        // Debug: Log the request details
        _logger.LogDebug("Generated request messages count: {Count}", request.Messages.Count);
        foreach (var msg in request.Messages)
        {
            if (msg.Content is TextContentPart textPart)
            {
                _logger.LogDebug("Message [{Role}]: {Content}", msg.Role, textPart.Text);
            }
        }

        // Generate text
        var generationResponse = await GenerateTextAsync(request.Model, generationRequest, cancellationToken);

        // Convert to OpenAI response format
        var completionId = $"chatcmpl-{Guid.NewGuid():N}";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var response = OpenAIConverter.ConvertToOpenAIResponse(generationResponse, request.Model, completionId, timestamp);

        // Parse response for potential tool calls
        if (request.Tools != null && request.Tools.Count > 0 && response.Choices.Count > 0)
        {
            var assistantMessage = response.Choices[0].Message;
            if (assistantMessage.Content is TextContentPart textContent)
            {
                var responseText = textContent.Text;

                // Debug: Log the raw response
                _logger.LogDebug("Raw model response: {Response}", responseText);

                // Try to parse tool calls from response (supports multiple calls)
                var toolCalls = ParseToolCalls(responseText, request.Tools);

                if (toolCalls.Count > 0)
                {
                    // Handle tool choice preference
                    if (ShouldExecuteToolCalls(request.ToolChoice, toolCalls))
                    {
                        // Check if we should execute tools or just return the calls
                        if (request.ParallelToolCalls != false) // Default is true for parallel execution
                        {
                            // Execute tool calls in parallel if enabled
                            await ExecuteToolCallsInParallelAsync(toolCalls, cancellationToken);
                        }

                        // Update the assistant message to include the tool calls
                        assistantMessage.ToolCalls = toolCalls;
                        assistantMessage.Content = null; // Remove content when tool_calls are present

                        // Set finish reason to tool_calls
                        response.Choices[0].FinishReason = "tool_calls";

                        _logger.LogInformation("Parsed {Count} tool calls from model response", toolCalls.Count);
                    }
                }
            }
        }

        return response;
    }

    /// <summary>
    /// Creates embeddings for the provided input (OpenAI-compatible)
    /// </summary>
    public async Task<OpenAIEmbeddingResponse> CreateEmbeddingsAsync(
        OpenAIEmbeddingRequest request,
        CancellationToken cancellationToken = default)
    {
        // Validate request
        ValidateEmbeddingRequest(request);

        // Check if the model exists and is loaded
        var model = await GetModelAsync(request.Model, cancellationToken);
        if (model == null)
        {
            throw new ArgumentException($"Model '{request.Model}' not found", nameof(request));
        }

        var isLoaded = await IsModelLoadedAsync(model.Id, cancellationToken);
        if (!isLoaded)
        {
            throw new InvalidOperationException($"Model '{request.Model}' is not loaded");
        }

        // Check if the model supports embeddings
        if (!model.Capabilities.SupportsEmbeddings)
        {
            throw new InvalidOperationException($"Model '{request.Model}' does not support embeddings");
        }

        // Convert OpenAI request to internal format
        var embeddingRequest = OpenAIConverter.ConvertToEmbeddingRequest(request);

        // Generate embeddings
        var embeddingResponse = await GenerateEmbeddingsAsync(request.Model, embeddingRequest, cancellationToken);

        // Convert to OpenAI response format
        var response = OpenAIConverter.ConvertToOpenAIEmbeddingResponse(embeddingResponse, request.Model);

        return response;
    }

    /// <summary>
    /// Creates a streaming chat completion (OpenAI-compatible)
    /// </summary>
    public async IAsyncEnumerable<string> CreateChatCompletionStreamAsync(
        OpenAIChatCompletionRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Validate request
        ValidateChatCompletionRequest(request);

        // Check if the model exists and is loaded
        var model = await GetModelAsync(request.Model, cancellationToken);
        if (model == null)
        {
            throw new ArgumentException($"Model '{request.Model}' not found", nameof(request));
        }

        var isLoaded = await IsModelLoadedAsync(model.Id, cancellationToken);
        if (!isLoaded)
        {
            throw new InvalidOperationException($"Model '{request.Model}' is not loaded");
        }

        // Check if the model supports text generation
        if (!model.Capabilities.SupportsTextGeneration)
        {
            throw new InvalidOperationException($"Model '{request.Model}' does not support text generation");
        }

        // Convert OpenAI request to internal format
        var generationRequest = OpenAIConverter.ConvertToGenerationRequest(request);
        generationRequest.Stream = true;

        // Generate streaming text
        await foreach (var chunk in GenerateTextStreamAsync(
            request.Model,
            generationRequest.Prompt,
            generationRequest.MaxTokens,
            generationRequest.Temperature,
            generationRequest.TopP,
            generationRequest.Parameters,
            cancellationToken))
        {
            // Format as OpenAI streaming response
            var completionId = $"chatcmpl-{Guid.NewGuid():N}";
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            var streamResponse = new
            {
                id = completionId,
                @object = "chat.completion.chunk",
                created = timestamp,
                model = request.Model,
                choices = new[]
                {
                    new
                    {
                        index = 0,
                        delta = new { content = chunk },
                        finish_reason = (string?)null // Cannot determine if complete from string chunk
                    }
                }
            };

            yield return System.Text.Json.JsonSerializer.Serialize(streamResponse);
        }
    }

    #endregion

    #region Dynamic Tool Formatting

    /// <summary>
    /// Apply model-specific tool formatting based on extracted metadata
    /// </summary>
    private async Task ApplyModelSpecificToolFormattingAsync(
        OpenAIChatCompletionRequest request,
        string modelId,
        CancellationToken cancellationToken)
    {
        try
        {
            // If metadata service is available, use dynamic formatting
            if (ModelMetadataService != null)
            {
                var metadata = await ModelMetadataService.GetModelMetadataAsync(modelId, cancellationToken);

                if (metadata.ToolCapabilities.SupportsToolCalling)
                {
                    await ApplyDynamicToolFormattingAsync(request, metadata, cancellationToken);
                    return;
                }
            }

            // Fallback to architecture-based formatting
            await ApplyArchitectureBasedToolFormattingAsync(request, modelId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply dynamic tool formatting, using fallback");
            await ApplyFallbackToolFormattingAsync(request, cancellationToken);
        }
    }

    /// <summary>
    /// Apply dynamic tool formatting based on extracted metadata and chat template
    /// </summary>
    private async Task ApplyDynamicToolFormattingAsync(
        OpenAIChatCompletionRequest request,
        ModelMetadata metadata,
        CancellationToken cancellationToken)
    {
        // If metadata service supports chat template application, use it
        if (ModelMetadataService != null)
        {
            try
            {
                await ApplyChatTemplateBasedToolFormattingAsync(request, metadata, cancellationToken);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to apply chat template based tool formatting, falling back to metadata-based approach");
            }
        }

        // Fallback to metadata-based formatting without hardcoded model name patterns
        await ApplyMetadataBasedToolFormattingAsync(request, metadata, cancellationToken);
    }

    /// <summary>
    /// Apply tool formatting using the model's native chat template system
    /// </summary>
    private async Task ApplyChatTemplateBasedToolFormattingAsync(
        OpenAIChatCompletionRequest request,
        ModelMetadata metadata,
        CancellationToken cancellationToken)
    {
        // Convert OpenAI request to chat template format
        var chatMessages = request.Messages.Select(m => new SDK.Services.ChatMessage
        {
            Role = m.Role.ToLowerInvariant(),
            Content = GetMessageContentText(m)
        }).ToList();

        // Prepare tool options for the chat template
        var toolOptions = new SDK.Services.ToolCallOptions
        {
            Tools = request.Tools!.Select(t => new SDK.Services.ToolDefinition
            {
                Name = t.Function.Name,
                Description = t.Function.Description ?? string.Empty,
                Parameters = t.Function.Parameters ?? new object()
            })
        };

        // Apply the model's native chat template with tool support
        var formattedPrompt = await ModelMetadataService!.ApplyChatTemplateAsync(
            request.Model,
            chatMessages,
            addGenerationPrompt: true,
            toolOptions,
            cancellationToken);

        // Replace the entire message array with a single formatted prompt
        request.Messages.Clear();
        request.Messages.Add(new OpenAIChatMessage
        {
            Role = "user",
            Content = new TextContentPart { Text = formattedPrompt }
        });

        _logger.LogDebug("Applied chat template based tool formatting for model: {Model}", request.Model);
    }

    /// <summary>
    /// Apply tool formatting based on metadata without hardcoded model patterns
    /// </summary>
    private Task ApplyMetadataBasedToolFormattingAsync(
        OpenAIChatCompletionRequest request,
        ModelMetadata metadata,
        CancellationToken cancellationToken)
    {
        var toolsJson = request.Tools!.Select(t => new
        {
            name = t.Function.Name,
            description = t.Function.Description,
            parameters = t.Function.Parameters
        }).ToArray();

        var toolsJsonString = System.Text.Json.JsonSerializer.Serialize(toolsJson);

        // Use the model's metadata-derived tool format instead of hardcoded patterns
        var toolFormat = metadata.ToolCapabilities.ToolCallFormat.ToLowerInvariant();

        // Apply format based on extracted capabilities rather than name matching
        string toolInstruction = DetermineToolInstructionFromMetadata(toolsJsonString, metadata);

        // Add to system message
        var systemMessage = request.Messages.FirstOrDefault(m => m.Role.ToLowerInvariant() == "system");
        if (systemMessage != null && systemMessage.Content is TextContentPart textContent)
        {
            textContent.Text += $"\n\n{toolInstruction}";
        }
        else
        {
            request.Messages.Insert(0, new OpenAIChatMessage
            {
                Role = "system",
                Content = new TextContentPart
                {
                    Text = $"You are a helpful assistant with some tools.\n\n{toolInstruction}"
                }
            });
        }

        _logger.LogDebug("Applied metadata-based tool formatting for format: {Format}", toolFormat);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Determine tool instruction format from metadata capabilities
    /// </summary>
    private string DetermineToolInstructionFromMetadata(string toolsJsonString, ModelMetadata metadata)
    {
        var format = metadata.ToolCapabilities.ToolCallFormat.ToLowerInvariant();
        var architecture = metadata.Architecture.ToLowerInvariant();

        // Use metadata-derived format with fallback chain
        return format switch
        {
            "phi4" or "phi3.5" or "phi3" => FormatToolsForPhi(toolsJsonString),
            "llama-native" or "llama" => FormatToolsForLlama(toolsJsonString),
            "mixtral" => FormatToolsForMixtral(toolsJsonString),
            "qwen" => FormatToolsForQwen(toolsJsonString),
            "gemma" => FormatToolsForGemma(toolsJsonString),
            _ => FormatToolsFromArchitecture(toolsJsonString, architecture, metadata)
        };
    }

    /// <summary>
    /// Format tools based on architecture when specific format is unknown
    /// </summary>
    private string FormatToolsFromArchitecture(string toolsJsonString, string architecture, ModelMetadata metadata)
    {
        // Extract tool tokens from metadata if available
        var toolTokens = metadata.ToolCapabilities.ToolTokens;

        return architecture switch
        {
            "phi3" or "phi" => FormatToolsForPhi(toolsJsonString),
            "llama" => FormatToolsForLlama(toolsJsonString),
            "mixtral" => FormatToolsForMixtral(toolsJsonString),
            "qwen" => FormatToolsForQwen(toolsJsonString),
            "gemma" => FormatToolsForGemma(toolsJsonString),
            _ => FormatToolsWithTokens(toolsJsonString, toolTokens)
        };
    }

    /// <summary>
    /// Format tools using extracted tool tokens from metadata
    /// </summary>
    private string FormatToolsWithTokens(string toolsJsonString, Dictionary<string, string> toolTokens)
    {
        if (toolTokens.Any())
        {
            // Use actual tokens from model metadata if available
            var startToken = toolTokens.GetValueOrDefault("start", "<tool>");
            var endToken = toolTokens.GetValueOrDefault("end", "</tool>");

            return $"Available tools: {toolsJsonString}\n\n" +
                   $"Use tools by responding with:\n{startToken}\n{{\"name\": \"function_name\", \"arguments\": {{...}}}}\n{endToken}";
        }

        // Generic fallback
        return FormatToolsGeneric(toolsJsonString);
    }

    /// <summary>
    /// Apply architecture-based tool formatting when metadata is not available
    /// This method attempts to infer capabilities from model name patterns as a fallback
    /// </summary>
    private async Task ApplyArchitectureBasedToolFormattingAsync(
        OpenAIChatCompletionRequest request,
        string modelId,
        CancellationToken cancellationToken)
    {
        // This is a compatibility fallback for when metadata extraction fails
        // Ideally, this should be avoided by ensuring metadata service works properly
        _logger.LogWarning("Falling back to architecture-based tool formatting for model: {ModelId}", modelId);

        // Try to get some metadata through alternative means
        ModelMetadata? inferredMetadata = null;
        if (ModelMetadataService != null)
        {
            try
            {
                inferredMetadata = await ModelMetadataService.GetModelMetadataAsync(modelId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not get metadata for model: {ModelId}", modelId);
            }
        }

        if (inferredMetadata != null)
        {
            // Use the metadata-based approach if we managed to get metadata
            await ApplyMetadataBasedToolFormattingAsync(request, inferredMetadata, cancellationToken);
            return;
        }

        // Last resort: use model name pattern inference
        await ApplyLegacyPatternBasedFormattingAsync(request, modelId, cancellationToken);
    }

    /// <summary>
    /// Legacy pattern-based formatting for maximum compatibility
    /// </summary>
    private Task ApplyLegacyPatternBasedFormattingAsync(
        OpenAIChatCompletionRequest request,
        string modelId,
        CancellationToken cancellationToken)
    {
        // Infer architecture from model name as absolute fallback
        var modelName = modelId.ToLowerInvariant();

        var toolsJson = request.Tools!.Select(t => new
        {
            name = t.Function.Name,
            description = t.Function.Description,
            parameters = t.Function.Parameters
        }).ToArray();

        var toolsJsonString = System.Text.Json.JsonSerializer.Serialize(toolsJson);

        // Create a minimal metadata-like object for consistency
        var legacyFormat = InferToolFormatFromModelName(modelName);
        string toolInstruction = legacyFormat switch
        {
            "phi4" or "phi3.5" or "phi3" => FormatToolsForPhi(toolsJsonString),
            "llama-native" => FormatToolsForLlama(toolsJsonString),
            "mixtral" => FormatToolsForMixtral(toolsJsonString),
            "qwen" => FormatToolsForQwen(toolsJsonString),
            "gemma" => FormatToolsForGemma(toolsJsonString),
            _ => FormatToolsGeneric(toolsJsonString)
        };

        // Add to system message
        var systemMessage = request.Messages.FirstOrDefault(m => m.Role.ToLowerInvariant() == "system");
        if (systemMessage != null && systemMessage.Content is TextContentPart textContent)
        {
            textContent.Text += $"\n\n{toolInstruction}";
        }
        else
        {
            request.Messages.Insert(0, new OpenAIChatMessage
            {
                Role = "system",
                Content = new TextContentPart
                {
                    Text = $"You are a helpful assistant with some tools.\n\n{toolInstruction}"
                }
            });
        }

        _logger.LogDebug("Applied legacy pattern-based tool formatting for inferred format: {Format}", legacyFormat);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Infer tool format from model name patterns (legacy compatibility)
    /// </summary>
    private static string InferToolFormatFromModelName(string modelName)
    {
        // More flexible architecture detection based on common naming patterns
        return modelName switch
        {
            var name when name.Contains("phi-4") || name.Contains("phi4") => "phi4",
            var name when name.Contains("phi-3.5") || name.Contains("phi3.5") => "phi3.5",
            var name when name.Contains("phi") => "phi3",
            var name when name.Contains("llama") => "llama-native",
            var name when name.Contains("mixtral") => "mixtral",
            var name when name.Contains("qwen") => "qwen",
            var name when name.Contains("gemma") => "gemma",
            _ => "generic"
        };
    }

    /// <summary>
    /// Apply fallback tool formatting when all else fails
    /// </summary>
    private Task ApplyFallbackToolFormattingAsync(
        OpenAIChatCompletionRequest request,
        CancellationToken cancellationToken)
    {
        var toolsJson = request.Tools!.Select(t => new
        {
            name = t.Function.Name,
            description = t.Function.Description,
            parameters = t.Function.Parameters
        }).ToArray();

        var toolsJsonString = System.Text.Json.JsonSerializer.Serialize(toolsJson);
        var toolInstruction = FormatToolsGeneric(toolsJsonString);

        // Add to system message
        var systemMessage = request.Messages.FirstOrDefault(m => m.Role.ToLowerInvariant() == "system");
        if (systemMessage != null && systemMessage.Content is TextContentPart textContent)
        {
            textContent.Text += $"\n\n{toolInstruction}";
        }
        else
        {
            request.Messages.Insert(0, new OpenAIChatMessage
            {
                Role = "system",
                Content = new TextContentPart
                {
                    Text = $"You are a helpful assistant with some tools.\n\n{toolInstruction}"
                }
            });
        }

        _logger.LogDebug("Applied fallback tool formatting");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Format tools for Phi models
    /// </summary>
    private string FormatToolsForPhi(string toolsJsonString)
    {
        return $"You have access to the following tools: {toolsJsonString}\n\n" +
               "When you need to use a tool, respond with the tool call in this exact format:\n" +
               "<|tool|>{{\"name\": \"function_name\", \"parameters\": {{\"param1\": \"value1\", \"param2\": \"value2\"}}}}<|/tool|>\n\n" +
               "Important:\n" +
               "- Use the exact function names from the tools list\n" +
               "- Parameters should match the required schema\n" +
               "- Only call tools when specifically requested or when they help answer the user's question\n" +
               "- Do not include any other text or explanation within the tool tags";
    }

    /// <summary>
    /// Format tools for Llama models
    /// </summary>
    private string FormatToolsForLlama(string toolsJsonString)
    {
        return $"Available tools: {toolsJsonString}\n\nTo use a tool, respond with: [TOOL_CALL] function_name(arguments) [/TOOL_CALL]";
    }

    /// <summary>
    /// Format tools for Mixtral models
    /// </summary>
    private string FormatToolsForMixtral(string toolsJsonString)
    {
        return $"Tools available: {toolsJsonString}\n\nUse tools by formatting your response as: {{\"tool_call\": {{\"name\": \"function_name\", \"arguments\": {{...}}}}}}";
    }

    /// <summary>
    /// Format tools with generic approach
    /// </summary>
    private string FormatToolsGeneric(string toolsJsonString)
    {
        return $"You have access to the following tools: {toolsJsonString}\n\nTo use a tool, respond with: TOOL_CALL: function_name(arguments)";
    }

    #endregion

    #region Tools Support

    /// <summary>
    /// Register a function tool
    /// </summary>
    public void RegisterTool(IFunctionTool tool)
    {
        ToolService.RegisterTool(tool);
    }

    /// <summary>
    /// Get all registered tools as OpenAI tool definitions
    /// </summary>
    public List<Tool> GetAvailableTools()
    {
        return ToolService.GetAvailableTools();
    }

    /// <summary>
    /// Execute a tool call
    /// </summary>
    public async Task<string> ExecuteToolAsync(string toolName, string argumentsJson, CancellationToken cancellationToken = default)
    {
        return await ToolService.ExecuteToolAsync(toolName, argumentsJson, cancellationToken);
    }

    /// <summary>
    /// Check if a tool is available
    /// </summary>
    public bool IsToolAvailable(string toolName)
    {
        return ToolService.IsToolAvailable(toolName);
    }

    #endregion

    #region Private Tool Parsing Methods

    /// <summary>
    /// Parse multiple tool calls from the model's response text
    /// </summary>
    private List<ToolCall> ParseToolCalls(string responseText, List<Tool> availableTools)
    {
        var toolCalls = new List<ToolCall>();
        var availableToolNames = availableTools.Select(t => t.Function.Name.ToLowerInvariant()).ToHashSet();

        try
        {
            // Try to parse Phi-4-mini specific tool call format with <|tool_call|> tokens
            var toolCallStartPattern = @"<\|tool_call\|>(.*?)<\|/tool_call\|>";
            var toolCallMatches = System.Text.RegularExpressions.Regex.Matches(responseText, toolCallStartPattern, System.Text.RegularExpressions.RegexOptions.Singleline);

            foreach (System.Text.RegularExpressions.Match match in toolCallMatches)
            {
                var toolCallContent = match.Groups[1].Value.Trim();
                _logger.LogDebug("Found tool call content: {Content}", toolCallContent);

                if (TryParseToolCallFromJson(toolCallContent, availableToolNames, out var toolCall))
                {
                    toolCalls.Add(toolCall);
                }
            }

            // If no structured tool calls found, try other patterns
            if (toolCalls.Count == 0)
            {
                // Look for TOOL_CALL: function_name(arguments) pattern
                if (TryParseSimpleToolCall(responseText, availableToolNames, out var simpleToolCall))
                {
                    toolCalls.Add(simpleToolCall);
                }
            }

            // Look for JSON patterns that might indicate tool calls
            if (toolCalls.Count == 0 && (responseText.Contains("tool_call") || responseText.Contains("function_call")))
            {
                if (TryParseJsonToolCall(responseText, availableToolNames, out var jsonToolCall))
                {
                    toolCalls.Add(jsonToolCall);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse tool calls from response: {Response}", responseText);
        }

        return toolCalls;
    }

    /// <summary>
    /// Try to parse a tool call from JSON content
    /// </summary>
    private bool TryParseToolCallFromJson(string toolCallContent, HashSet<string> availableToolNames, out ToolCall toolCall)
    {
        toolCall = null!;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(toolCallContent);
            var root = doc.RootElement;

            var name = root.GetProperty("name").GetString();
            var arguments = root.TryGetProperty("arguments", out var argsElement) ? argsElement.GetRawText() : "{}";

            if (!string.IsNullOrEmpty(name) && availableToolNames.Contains(name.ToLowerInvariant()))
            {
                toolCall = CreateToolCall(name, arguments);
                return true;
            }
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogDebug(ex, "Failed to parse tool call as JSON: {Content}", toolCallContent);
        }

        return false;
    }

    /// <summary>
    /// Try to parse simple TOOL_CALL: function_name(arguments) format
    /// </summary>
    private bool TryParseSimpleToolCall(string responseText, HashSet<string> availableToolNames, out ToolCall toolCall)
    {
        toolCall = null!;

        if (responseText.Contains("TOOL_CALL:"))
        {
            var toolCallStart = responseText.IndexOf("TOOL_CALL:");
            if (toolCallStart >= 0)
            {
                var callLine = responseText.Substring(toolCallStart + "TOOL_CALL:".Length).Trim();
                var parenIndex = callLine.IndexOf('(');

                if (parenIndex > 0)
                {
                    var functionName = callLine.Substring(0, parenIndex).Trim();

                    if (availableToolNames.Contains(functionName.ToLowerInvariant()))
                    {
                        var argsStart = parenIndex + 1;
                        var parenEnd = callLine.LastIndexOf(')');
                        var jsonArgs = "{}";

                        if (parenEnd > argsStart)
                        {
                            var argsString = callLine.Substring(argsStart, parenEnd - argsStart).Trim();
                            if (!string.IsNullOrEmpty(argsString))
                            {
                                jsonArgs = ConvertSimpleArgsToJson(argsString, functionName);
                            }
                        }

                        toolCall = CreateToolCall(functionName, jsonArgs);
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Try to parse JSON-formatted tool call from response
    /// </summary>
    private bool TryParseJsonToolCall(string responseText, HashSet<string> availableToolNames, out ToolCall toolCall)
    {
        toolCall = null!;

        var jsonStart = responseText.IndexOf('{');
        var jsonEnd = responseText.LastIndexOf('}');

        if (jsonStart >= 0 && jsonEnd > jsonStart)
        {
            try
            {
                var jsonStr = responseText.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var toolCallData = System.Text.Json.JsonSerializer.Deserialize<dynamic>(jsonStr);

                if (toolCallData != null)
                {
                    var toolCallElement = ((System.Text.Json.JsonElement)toolCallData).GetProperty("tool_call");
                    var name = toolCallElement.GetProperty("name").GetString();
                    var arguments = toolCallElement.GetProperty("arguments");

                    if (!string.IsNullOrEmpty(name) && availableToolNames.Contains(name.ToLowerInvariant()))
                    {
                        toolCall = CreateToolCall(name, arguments.GetRawText());
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to parse JSON tool call: {Json}", responseText.Substring(jsonStart, jsonEnd - jsonStart + 1));
            }
        }

        return false;
    }

    /// <summary>
    /// Create a properly formatted tool call with OpenAI-compatible ID
    /// </summary>
    private ToolCall CreateToolCall(string functionName, string arguments)
    {
        return new ToolCall
        {
            Id = $"call_{Guid.NewGuid():N}",
            Type = "function",
            Function = new FunctionCall
            {
                Name = functionName,
                Arguments = arguments
            }
        };
    }

    /// <summary>
    /// Convert simple argument format to JSON
    /// </summary>
    private string ConvertSimpleArgsToJson(string argsString, string functionName)
    {
        // Simple parsing for key="value" format
        if (argsString.Contains("location=") && functionName == "get_weather")
        {
            var locationMatch = System.Text.RegularExpressions.Regex.Match(argsString, @"location\s*=\s*""([^""]+)""");
            if (locationMatch.Success)
            {
                return $"{{\"location\": \"{locationMatch.Groups[1].Value}\"}}";
            }
        }
        else if (functionName == "get_time")
        {
            return "{}";
        }

        // Try to parse as JSON if it looks like JSON
        if (argsString.Trim().StartsWith('{') && argsString.Trim().EndsWith('}'))
        {
            return argsString.Trim();
        }

        return "{}";
    }

    /// <summary>
    /// Determine if tool calls should be executed based on tool_choice setting
    /// </summary>
    private bool ShouldExecuteToolCalls(ToolChoice? toolChoice, List<ToolCall> toolCalls)
    {
        if (toolChoice == null)
            return true; // Default behavior

        if (toolChoice.Type == "none")
            return false;

        if (toolChoice.Type == "auto")
            return true;

        if (toolChoice.Type == "required")
            return true;

        // For specific function choice, check if any tool call matches
        if (toolChoice.Function != null)
        {
            return toolCalls.Any(tc => tc.Function.Name.Equals(toolChoice.Function.Name, StringComparison.OrdinalIgnoreCase));
        }

        return true;
    }

    /// <summary>
    /// Execute multiple tool calls in parallel
    /// </summary>
    private async Task ExecuteToolCallsInParallelAsync(List<ToolCall> toolCalls, CancellationToken cancellationToken)
    {
        if (toolCalls.Count <= 1)
        {
            // Single tool call - execute directly
            if (toolCalls.Count == 1)
            {
                await ExecuteSingleToolCallAsync(toolCalls[0], cancellationToken);
            }
            return;
        }

        // Execute multiple tool calls in parallel
        var tasks = toolCalls.Select(tc => ExecuteSingleToolCallAsync(tc, cancellationToken));
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Execute a single tool call
    /// </summary>
    private async Task ExecuteSingleToolCallAsync(ToolCall toolCall, CancellationToken cancellationToken)
    {
        try
        {
            var toolResult = await ToolService.ExecuteToolAsync(
                toolCall.Function.Name,
                toolCall.Function.Arguments,
                cancellationToken);

            _logger.LogDebug("Tool call {Id} executed successfully: {Name}", toolCall.Id, toolCall.Function.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute tool call {Id}: {Name}", toolCall.Id, toolCall.Function.Name);
            // Continue execution of other tools even if one fails
        }
    }

    /// <summary>
    /// Try to parse a tool call from the model's response text (legacy method for backward compatibility)
    /// </summary>
    private bool TryParseToolCall(string responseText, out ToolCall toolCall)
    {
        toolCall = null!;

        try
        {
            // Try to parse Phi-4-mini specific tool call format with <|tool_call|> tokens
            var toolCallStartPattern = @"<\|tool_call\|>(.*?)<\|/tool_call\|>";
            var toolCallMatch = System.Text.RegularExpressions.Regex.Match(responseText, toolCallStartPattern, System.Text.RegularExpressions.RegexOptions.Singleline);

            if (toolCallMatch.Success)
            {
                var toolCallContent = toolCallMatch.Groups[1].Value.Trim();
                _logger.LogDebug("Found tool call content: {Content}", toolCallContent);

                // Try to parse as JSON
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(toolCallContent);
                    var root = doc.RootElement;

                    var name = root.GetProperty("name").GetString();
                    var arguments = root.TryGetProperty("arguments", out var argsElement) ? argsElement.GetRawText() : "{}";

                    if (!string.IsNullOrEmpty(name))
                    {
                        toolCall = CreateToolCall(name, arguments);
                        return true;
                    }
                }
                catch (System.Text.Json.JsonException ex)
                {
                    _logger.LogDebug(ex, "Failed to parse tool call as JSON: {Content}", toolCallContent);
                }
            }

            // Look for the simple pattern: TOOL_CALL: function_name(arguments)
            if (responseText.Contains("TOOL_CALL:"))
            {
                // Extract the tool call part
                var toolCallStart = responseText.IndexOf("TOOL_CALL:");
                if (toolCallStart >= 0)
                {
                    var callLine = responseText.Substring(toolCallStart + "TOOL_CALL:".Length).Trim();

                    // Parse function_name(arguments) format
                    var parenIndex = callLine.IndexOf('(');
                    if (parenIndex > 0)
                    {
                        var functionName = callLine.Substring(0, parenIndex).Trim();
                        var argsStart = parenIndex + 1;
                        var parenEnd = callLine.LastIndexOf(')');

                        if (parenEnd > argsStart)
                        {
                            var argsString = callLine.Substring(argsStart, parenEnd - argsStart).Trim();

                            // Create a simple JSON object from the arguments
                            var jsonArgs = ConvertSimpleArgsToJson(argsString, functionName);

                            toolCall = CreateToolCall(functionName, jsonArgs);
                            return true;
                        }
                    }
                }
            }

            // Fallback: Look for JSON patterns that might indicate a tool call
            if (responseText.Contains("tool_call") || responseText.Contains("function_call"))
            {
                // Try to extract JSON from the response
                var jsonStart = responseText.IndexOf('{');
                var jsonEnd = responseText.LastIndexOf('}');

                if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                    var jsonStr = responseText.Substring(jsonStart, jsonEnd - jsonStart + 1);

                    // Try to parse as our expected tool call format
                    var toolCallData = System.Text.Json.JsonSerializer.Deserialize<dynamic>(jsonStr);

                    if (toolCallData != null)
                    {
                        // Extract tool call information
                        var toolCallElement = ((System.Text.Json.JsonElement)toolCallData).GetProperty("tool_call");
                        var name = toolCallElement.GetProperty("name").GetString();
                        var arguments = toolCallElement.GetProperty("arguments");

                        if (!string.IsNullOrEmpty(name))
                        {
                            toolCall = CreateToolCall(name, arguments.GetRawText());
                            return true;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse potential tool call from response: {Response}", responseText);
        }

        return false;
    }

    #endregion

    #region Private Helper Methods

    /// <summary>
    /// Extract text content from an OpenAI message
    /// </summary>
    private static string GetMessageContentText(OpenAIChatMessage message)
    {
        if (message.Content is TextContentPart textPart)
        {
            return textPart.Text ?? string.Empty;
        }

        // For other content types, try to extract string representation
        return message.Content?.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Format tools for Qwen models
    /// </summary>
    private string FormatToolsForQwen(string toolsJsonString)
    {
        return $"# Available Tools\n\n{toolsJsonString}\n\n" +
               "To use a tool, respond with the following format:\n" +
               "```json\n{{\"tool_call\": {{\"name\": \"function_name\", \"arguments\": {{...}}}}}}\n```";
    }

    /// <summary>
    /// Format tools for Gemma models
    /// </summary>
    private string FormatToolsForGemma(string toolsJsonString)
    {
        return $"Available tools: {toolsJsonString}\n\n" +
               "Use tools by responding with:\n" +
               "<tool_call>{{\"name\": \"function_name\", \"arguments\": {{...}}}}</tool_call>";
    }

    #endregion

    #region Private Validation Methods

    private static void ValidateChatCompletionRequest(OpenAIChatCompletionRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        if (string.IsNullOrEmpty(request.Model))
            throw new ArgumentException("Model is required", nameof(request));

        if (request.Messages == null || request.Messages.Count == 0)
            throw new ArgumentException("Messages are required", nameof(request));

        // Validate messages
        foreach (var message in request.Messages)
        {
            if (string.IsNullOrEmpty(message.Role))
                throw new ArgumentException("Message role is required", nameof(request));

            // Content is required for most roles except assistant with tool_calls
            if (message.Content == null &&
                (message.Role.ToLowerInvariant() != "assistant" || message.ToolCalls == null || message.ToolCalls.Count == 0))
            {
                throw new ArgumentException("Message content is required", nameof(request));
            }

            // Validate role values
            var validRoles = new[] { "system", "user", "assistant", "tool", "developer" };
            if (!validRoles.Contains(message.Role.ToLowerInvariant()))
            {
                throw new ArgumentException($"Invalid message role '{message.Role}'. Must be one of: {string.Join(", ", validRoles)}", nameof(request));
            }

            // Validate tool message requirements
            if (message.Role.ToLowerInvariant() == "tool" && string.IsNullOrEmpty(message.ToolCallId))
            {
                throw new ArgumentException("Tool messages must include tool_call_id", nameof(request));
            }
        }

        // Validate parameter ranges
        if (request.Temperature.HasValue && (request.Temperature < 0 || request.Temperature > 2))
            throw new ArgumentOutOfRangeException(nameof(request), "Temperature must be between 0 and 2");

        if (request.TopP.HasValue && (request.TopP <= 0 || request.TopP > 1))
            throw new ArgumentOutOfRangeException(nameof(request), "Top-p must be between 0 and 1");

        if (request.MaxCompletionTokens.HasValue && request.MaxCompletionTokens <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Max completion tokens must be greater than 0");
    }

    private static void ValidateEmbeddingRequest(OpenAIEmbeddingRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        if (string.IsNullOrEmpty(request.Model))
            throw new ArgumentException("Model is required", nameof(request));

        if (request.Input == null)
            throw new ArgumentException("Input is required", nameof(request));
    }

    #endregion
}
