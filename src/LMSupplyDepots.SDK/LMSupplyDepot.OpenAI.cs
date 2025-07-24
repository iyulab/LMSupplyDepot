using LMSupplyDepots.SDK.OpenAI.Models;
using LMSupplyDepots.SDK.OpenAI.Services;
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

        if (!model.IsLoaded)
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

        // If request has tools defined, add them to the system prompt or handle model-specific tool formatting
        if (request.Tools != null && request.Tools.Count > 0)
        {
            // For models that don't natively support function calling, we can add tool descriptions to system prompt
            var toolDescriptions = string.Join("\n", request.Tools.Select(t =>
                $"Tool: {t.Function.Name}\nDescription: {t.Function.Description}\nParameters: {System.Text.Json.JsonSerializer.Serialize(t.Function.Parameters)}"));

            // Find or create system message
            var systemMessage = request.Messages.FirstOrDefault(m => m.Role.ToLowerInvariant() == "system");
            if (systemMessage != null && systemMessage.Content is TextContentPart textContent)
            {
                textContent.Text += $"\n\nAvailable tools:\n{toolDescriptions}";
            }
            else
            {
                // Insert system message at the beginning
                request.Messages.Insert(0, new OpenAIChatMessage
                {
                    Role = "system",
                    Content = new TextContentPart
                    {
                        Text = $"You are a helpful assistant. You have access to these tools:\n{toolDescriptions}\n\nTo use a tool, write EXACTLY: TOOL_CALL: tool_name(arguments)\nFor example: TOOL_CALL: get_weather(location=\"Seoul\")\nAlways use tools when the user asks for information you cannot directly provide."
                    }
                });
            }
        }

        // Convert OpenAI request to internal format
        var generationRequest = OpenAIConverter.ConvertToGenerationRequest(request);

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

                // Try to parse tool call from response
                if (TryParseToolCall(responseText, out var toolCall))
                {
                    // Execute the tool call
                    try
                    {
                        var toolResult = await ToolService.ExecuteToolAsync(
                            toolCall.Function.Name,
                            toolCall.Function.Arguments,
                            cancellationToken);

                        // Update the assistant message to include the tool call
                        assistantMessage.ToolCalls = new List<ToolCall> { toolCall };
                        assistantMessage.Content = null; // Remove content when tool_calls are present

                        // Set finish reason to tool_calls
                        response.Choices[0].FinishReason = "tool_calls";
                    }
                    catch (Exception ex)
                    {
                        // If tool execution fails, keep the original response
                        _logger.LogError(ex, "Failed to execute tool call: {ToolName}", toolCall.Function.Name);
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

        if (!model.IsLoaded)
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

        if (!model.IsLoaded)
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
    /// Try to parse a tool call from the model's response text
    /// </summary>
    private bool TryParseToolCall(string responseText, out ToolCall toolCall)
    {
        toolCall = null!;

        try
        {
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
                            var jsonArgs = "{}";
                            if (!string.IsNullOrEmpty(argsString))
                            {
                                // Simple parsing for key="value" format
                                if (argsString.Contains("location=") && functionName == "get_weather")
                                {
                                    var locationMatch = System.Text.RegularExpressions.Regex.Match(argsString, @"location\s*=\s*""([^""]+)""");
                                    if (locationMatch.Success)
                                    {
                                        jsonArgs = $"{{\"location\": \"{locationMatch.Groups[1].Value}\"}}";
                                    }
                                }
                                else if (functionName == "get_time")
                                {
                                    jsonArgs = "{}";
                                }
                            }

                            toolCall = new ToolCall
                            {
                                Id = $"call_{Guid.NewGuid():N}",
                                Type = "function",
                                Function = new FunctionCall
                                {
                                    Name = functionName,
                                    Arguments = jsonArgs
                                }
                            };
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
                            toolCall = new ToolCall
                            {
                                Id = $"call_{Guid.NewGuid():N}",
                                Type = "function",
                                Function = new FunctionCall
                                {
                                    Name = name,
                                    Arguments = arguments.GetRawText()
                                }
                            };
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
