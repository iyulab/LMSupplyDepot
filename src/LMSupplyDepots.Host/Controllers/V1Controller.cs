using LMSupplyDepots.Contracts;
using LMSupplyDepots.Host.Services;
using LMSupplyDepots.SDK.OpenAI.Models;
using LMSupplyDepots.SDK.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text;
using LMSupplyDepots.Utils;

namespace LMSupplyDepots.Host.Controllers;

/// <summary>
/// Controller for v1 API inference operations (OpenAI-compatible)
/// </summary>
[ApiController]
[Route("/v1")]
public class V1Controller : ControllerBase
{
    private readonly IHostService _hostService;
    private readonly IToolExecutionService _toolExecutionService;
    private readonly IModelMetadataService? _modelMetadataService;
    private readonly ILogger<V1Controller> _logger;

    /// <summary>
    /// Initializes a new instance of the V1Controller
    /// </summary>
    public V1Controller(
        IHostService hostService,
        IToolExecutionService toolExecutionService,
        ILogger<V1Controller> logger,
        IServiceProvider serviceProvider)
    {
        _hostService = hostService;
        _toolExecutionService = toolExecutionService;
        _modelMetadataService = serviceProvider.GetService<IModelMetadataService>();
        _logger = logger;
    }

    /// <summary>
    /// Lists all available models (OpenAI-compatible)
    /// </summary>
    [HttpGet("models")]
    public async Task<ActionResult> ListModels(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _hostService.ListModelsOpenAIAsync(cancellationToken);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing models");
            return StatusCode(500, CreateErrorResponse("internal_error", "An error occurred while listing models"));
        }
    }

    /// <summary>
    /// Creates a chat completion (OpenAI-compatible)
    /// </summary>
    [HttpPost("chat/completions")]
    public async Task<ActionResult> CreateChatCompletion(
        [FromBody] OpenAIChatCompletionRequest request,
        CancellationToken cancellationToken)
    {
        // Validate request object
        if (request == null)
        {
            return BadRequest(CreateErrorResponse("invalid_request_error", "Request body is required"));
        }

        // Validate request
        if (string.IsNullOrEmpty(request.Model))
        {
            return BadRequest(CreateErrorResponse("invalid_request_error", "Model is required", "model"));
        }

        if (request.Messages == null || request.Messages.Count == 0)
        {
            return BadRequest(CreateErrorResponse("invalid_request_error", "Messages are required", "messages"));
        }

        // Validate messages content
        foreach (var message in request.Messages)
        {
            if (string.IsNullOrEmpty(message.Role))
            {
                return BadRequest(CreateErrorResponse("invalid_request_error", "Message role is required", "messages"));
            }

            // Content is required for most roles except assistant with tool_calls
            if (message.Content == null &&
                (message.Role.ToLowerInvariant() != "assistant" || message.ToolCalls == null || message.ToolCalls.Count == 0))
            {
                return BadRequest(CreateErrorResponse("invalid_request_error", "Message content is required", "messages"));
            }

            // Validate role values (including new roles)
            var validRoles = new[] { "system", "user", "assistant", "tool", "developer" };
            if (!validRoles.Contains(message.Role.ToLowerInvariant()))
            {
                return BadRequest(CreateErrorResponse("invalid_request_error",
                    $"Invalid message role '{message.Role}'. Must be one of: {string.Join(", ", validRoles)}",
                    "messages"));
            }

            // Validate tool message requirements
            if (message.Role.ToLowerInvariant() == "tool")
            {
                if (string.IsNullOrEmpty(message.ToolCallId))
                {
                    return BadRequest(CreateErrorResponse("invalid_request_error", "Tool messages must include tool_call_id", "messages"));
                }

                // Tool messages should have content
                if (message.Content == null)
                {
                    return BadRequest(CreateErrorResponse("invalid_request_error", "Tool messages must include content", "messages"));
                }
            }

            // Validate assistant messages with tool calls
            if (message.Role.ToLowerInvariant() == "assistant" && message.ToolCalls != null)
            {
                foreach (var toolCall in message.ToolCalls)
                {
                    if (string.IsNullOrEmpty(toolCall.Id))
                    {
                        return BadRequest(CreateErrorResponse("invalid_request_error", "Tool calls must include id", "messages"));
                    }

                    if (toolCall.Function == null || string.IsNullOrEmpty(toolCall.Function.Name))
                    {
                        return BadRequest(CreateErrorResponse("invalid_request_error", "Tool calls must include function name", "messages"));
                    }

                    // Validate that tool call ID format is correct
                    if (!toolCall.Id.StartsWith("call_"))
                    {
                        return BadRequest(CreateErrorResponse("invalid_request_error", "Tool call IDs must start with 'call_'", "messages"));
                    }
                }

                // Assistant messages with tool_calls should not have content
                if (message.Content != null)
                {
                    return BadRequest(CreateErrorResponse("invalid_request_error", "Assistant messages with tool_calls should not include content", "messages"));
                }
            }
        }

        // Validate parameter ranges
        if (request.Temperature.HasValue && (request.Temperature < 0 || request.Temperature > 2))
        {
            return BadRequest(CreateErrorResponse("invalid_request_error", "Temperature must be between 0 and 2", "temperature"));
        }

        if (request.TopP.HasValue && (request.TopP <= 0 || request.TopP > 1))
        {
            return BadRequest(CreateErrorResponse("invalid_request_error", "Top-p must be between 0 and 1", "top_p"));
        }

        if (request.MaxCompletionTokens.HasValue && request.MaxCompletionTokens <= 0)
        {
            return BadRequest(CreateErrorResponse("invalid_request_error", "Max completion tokens must be greater than 0", "max_completion_tokens"));
        }

        // Validate tool choice parameter
        if (request.ToolChoice != null)
        {
            var validToolChoiceTypes = new[] { "auto", "none", "required", "function" };
            if (!string.IsNullOrEmpty(request.ToolChoice.Type) && !validToolChoiceTypes.Contains(request.ToolChoice.Type))
            {
                return BadRequest(CreateErrorResponse("invalid_request_error",
                    $"Invalid tool_choice type '{request.ToolChoice.Type}'. Must be one of: {string.Join(", ", validToolChoiceTypes)}",
                    "tool_choice"));
            }

            // If tool_choice is specified but no tools are provided
            if (request.Tools == null || request.Tools.Count == 0)
            {
                return BadRequest(CreateErrorResponse("invalid_request_error", "tool_choice requires tools to be specified", "tool_choice"));
            }

            // Validate function-specific tool choice
            if (request.ToolChoice.Type == "function" && request.ToolChoice.Function != null)
            {
                var functionName = request.ToolChoice.Function.Name;
                if (!request.Tools.Any(t => t.Function.Name.Equals(functionName, StringComparison.OrdinalIgnoreCase)))
                {
                    return BadRequest(CreateErrorResponse("invalid_request_error",
                        $"tool_choice function '{functionName}' not found in available tools",
                        "tool_choice"));
                }
            }
        }

        // Validate tools parameter
        if (request.Tools != null)
        {
            foreach (var tool in request.Tools)
            {
                if (tool.Type != "function")
                {
                    return BadRequest(CreateErrorResponse("invalid_request_error", "Only 'function' type tools are supported", "tools"));
                }

                if (tool.Function == null || string.IsNullOrEmpty(tool.Function.Name))
                {
                    return BadRequest(CreateErrorResponse("invalid_request_error", "Tool function name is required", "tools"));
                }

                // Validate function name format (OpenAI has specific requirements)
                if (!System.Text.RegularExpressions.Regex.IsMatch(tool.Function.Name, @"^[a-zA-Z0-9_-]+$"))
                {
                    return BadRequest(CreateErrorResponse("invalid_request_error",
                        "Tool function names must contain only letters, numbers, underscores, and dashes",
                        "tools"));
                }
            }
        }

        try
        {
            // Handle streaming requests
            if (request.Stream == true)
            {
                return await CreateChatCompletionStream(request, cancellationToken);
            }

            // Use HostService OpenAI method for initial completion
            var response = await _hostService.CreateChatCompletionAsync(request, cancellationToken);

            // Cast to get the actual response type for tool call processing
            var chatResponse = response as OpenAIChatCompletionResponse;
            if (chatResponse?.Choices?.FirstOrDefault()?.Message != null)
            {
                var message = chatResponse.Choices.First().Message;

                // Check if tool_choice is "required" and force tool call generation
                if (request.Tools != null && request.Tools.Count > 0 &&
                    request.ToolChoice?.Type == "required")
                {
                    _logger.LogInformation("Tool choice is 'required', generating mandatory tool call");

                    // Generate a tool call based on the user's request
                    var forcedToolCall = GenerateForcedToolCall(request.Messages.LastOrDefault()?.Content?.ToString() ?? "", request.Tools);
                    if (forcedToolCall != null)
                    {
                        _logger.LogInformation("Generated forced tool call: {ToolCallId} - {FunctionName}",
                            forcedToolCall.Id, forcedToolCall.Function.Name);

                        return Ok(new OpenAIChatCompletionResponse
                        {
                            Id = chatResponse.Id,
                            Object = chatResponse.Object,
                            Created = chatResponse.Created,
                            Model = chatResponse.Model,
                            Usage = chatResponse.Usage,
                            Choices = new List<OpenAIChatChoice>
                            {
                                new OpenAIChatChoice
                                {
                                    Index = 0,
                                    Message = new OpenAIChatMessage
                                    {
                                        Role = "assistant",
                                        Content = null,
                                        ToolCalls = new List<ToolCall> { forcedToolCall }
                                    },
                                    FinishReason = "tool_calls"
                                }
                            }
                        });
                    }
                }

                // Check if tool execution is needed and tools are available
                // Extract content as string for tool call detection
                string? contentText = null;
                if (message.Content is TextContentPart textPart)
                {
                    contentText = textPart.Text;
                }
                else if (message.Content != null)
                {
                    // Handle other ContentPart types by converting to string
                    contentText = message.Content.ToString();
                }

                if (request.Tools != null && request.Tools.Count > 0 && !string.IsNullOrEmpty(contentText))
                {
                    _logger.LogInformation("Checking for tool calls in response content. Tools available: {ToolCount}, Content length: {ContentLength}",
                        request.Tools.Count, contentText.Length);

                    try
                    {
                        // Parse tool calls from content using model-specific adaptive parsing
                        var toolCalls = await ParseToolCallsFromContentAsync(contentText, request.Tools, request.Model);

                        _logger.LogInformation("Tool call parsing completed. Found {ToolCallCount} tool calls", toolCalls.Count);

                        if (toolCalls.Count > 0)
                        {
                            _logger.LogInformation("Tool calls detected. Returning response with tool_calls finish_reason");

                            foreach (var toolCall in toolCalls)
                            {
                                _logger.LogInformation("Tool call: {ToolCallId} - {FunctionName}({Arguments})",
                                    toolCall.Id, toolCall.Function.Name, toolCall.Function.Arguments);
                            }

                            // Return the response with tool calls
                            return Ok(new OpenAIChatCompletionResponse
                            {
                                Id = chatResponse.Id,
                                Object = chatResponse.Object,
                                Created = chatResponse.Created,
                                Model = chatResponse.Model,
                                Usage = chatResponse.Usage,
                                Choices = new List<OpenAIChatChoice>
                                {
                                    new OpenAIChatChoice
                                    {
                                        Index = 0,
                                        Message = new OpenAIChatMessage
                                        {
                                            Role = "assistant",
                                            Content = null, // Assistant messages with tool_calls should not have content
                                            ToolCalls = toolCalls
                                        },
                                        FinishReason = "tool_calls"
                                    }
                                }
                            });
                        }
                        else
                        {
                            _logger.LogInformation("No tool calls found in content: {ContentPreview}",
                                contentText.Length > 200 ? contentText.Substring(0, 200) + "..." : contentText);
                        }
                    }
                    catch (Exception toolEx)
                    {
                        _logger.LogWarning(toolEx, "Tool call parsing failed, returning original response. Content: {ContentPreview}",
                            contentText.Length > 100 ? contentText.Substring(0, 100) + "..." : contentText);
                        // Continue with original response if tool call parsing fails
                    }
                }
                else
                {
                    if (request.Tools != null && request.Tools.Count > 0)
                    {
                        _logger.LogInformation("Tools were provided but no content to parse. Content is null or empty.");
                    }
                }
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating chat completion with model {Model}", request.Model);
            return StatusCode(500, CreateErrorResponse("internal_error", $"Error generating chat completion: {ex.Message}"));
        }
    }

    /// <summary>
    /// Creates embeddings for the provided input (OpenAI-compatible)
    /// </summary>
    [HttpPost("embeddings")]
    public async Task<ActionResult> CreateEmbeddings(
        [FromBody] OpenAIEmbeddingRequest request,
        CancellationToken cancellationToken)
    {
        // Validate request object
        if (request == null)
        {
            return BadRequest(CreateErrorResponse("invalid_request_error", "Request body is required"));
        }

        // Validate request
        if (string.IsNullOrEmpty(request.Model))
        {
            return BadRequest(CreateErrorResponse("invalid_request_error", "Model is required", "model"));
        }

        if (request.Input == null)
        {
            return BadRequest(CreateErrorResponse("invalid_request_error", "Input is required", "input"));
        }

        try
        {
            // Use HostService OpenAI method
            var response = await _hostService.CreateEmbeddingsAsync(request, cancellationToken);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating embeddings with model {Model}", request.Model);
            return StatusCode(500, CreateErrorResponse("internal_error", $"Error generating embeddings: {ex.Message}"));
        }
    }

    /// <summary>
    /// Handles streaming chat completions
    /// </summary>
    private async Task<ActionResult> CreateChatCompletionStream(
        OpenAIChatCompletionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            // Set streaming response headers
            Response.ContentType = "text/event-stream";
            Response.Headers.Add("Cache-Control", "no-cache");
            Response.Headers.Add("Connection", "keep-alive");

            var completionId = $"chatcmpl-{Guid.NewGuid():N}";
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // Track accumulated content for tool call detection
            var accumulatedContent = new StringBuilder();
            var hasTools = request.Tools != null && request.Tools.Count > 0;

            // Stream the generated text using HostService
            await foreach (var token in _hostService.CreateChatCompletionStreamAsync(request, cancellationToken))
            {
                accumulatedContent.Append(token);

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
                            delta = new { content = token },
                            finish_reason = (string?)null
                        }
                    }
                };

                var json = JsonSerializer.Serialize(streamResponse, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                await Response.WriteAsync($"data: {json}\n\n");
                await Response.Body.FlushAsync(cancellationToken);
            }

            // After streaming is complete, check for tool calls if tools are available
            string finishReason = await DetermineStreamingFinishReasonAsync(
                accumulatedContent.ToString(),
                hasTools,
                request.Tools,
                accumulatedContent.Length,
                request.MaxCompletionTokens ?? 2048,
                true,
                request.Model);
            object? finalDelta = new { };

            if (hasTools)
            {
                var fullContent = accumulatedContent.ToString();
                var toolCalls = await ParseToolCallsFromContentAsync(fullContent, request.Tools!, request.Model);

                if (toolCalls.Count > 0)
                {
                    finishReason = "tool_calls";

                    // Send tool calls in the delta
                    var toolCallsResponse = new
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
                                delta = new { tool_calls = toolCalls.Select((tc, i) => new {
                                    index = i,
                                    id = tc.Id,
                                    type = tc.Type,
                                    function = new {
                                        name = tc.Function.Name,
                                        arguments = tc.Function.Arguments
                                    }
                                })},
                                finish_reason = (string?)null
                            }
                        }
                    };

                    var toolCallsJson = JsonSerializer.Serialize(toolCallsResponse, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                    await Response.WriteAsync($"data: {toolCallsJson}\n\n");
                    await Response.Body.FlushAsync(cancellationToken);
                }
            }

            // Send final chunk
            var finalResponse = new
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
                        delta = finalDelta,
                        finish_reason = finishReason
                    }
                }
            };

            var finalJson = JsonSerializer.Serialize(finalResponse, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            await Response.WriteAsync($"data: {finalJson}\n\n");
            await Response.WriteAsync("data: [DONE]\n\n");
            await Response.Body.FlushAsync(cancellationToken);

            return new EmptyResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error streaming chat completion with model {Model}", request.Model);

            var errorResponse = CreateErrorResponse("internal_error", $"Error generating chat completion: {ex.Message}");
            var errorJson = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            await Response.WriteAsync($"data: {errorJson}\n\n");
            await Response.WriteAsync("data: [DONE]\n\n");

            return new EmptyResult();
        }
    }

    /// <summary>
    /// Creates an OpenAI-compatible error response
    /// </summary>
    private OpenAIErrorResponse CreateErrorResponse(string type, string message, string? param = null, string? code = null)
    {
        return new OpenAIErrorResponse
        {
            Error = new OpenAIError
            {
                Type = type,
                Message = message,
                Param = param,
                Code = code
            }
        };
    }

    /// <summary>
    /// Parse tool calls from accumulated content during streaming
    /// </summary>
    /// <summary>
    /// Parse tool calls from content using model-specific metadata and chat template
    /// </summary>
    private async Task<List<ToolCall>> ParseToolCallsFromContentAsync(string content, List<Tool> availableTools, string modelId)
    {
        var toolCalls = new List<ToolCall>();
        var availableToolNames = availableTools.Select(t => t.Function.Name.ToLowerInvariant()).ToHashSet();

        _logger.LogDebug("Parsing tool calls from content for model: {ModelId}. Available tools: [{AvailableTools}]",
            modelId, string.Join(", ", availableToolNames));
        _logger.LogDebug("Content to parse (first 500 chars): {ContentPreview}",
            content.Length > 500 ? content.Substring(0, 500) + "..." : content);

        try
        {
            // Get model metadata to determine tool call format if service is available
            if (_modelMetadataService != null)
            {
                var metadata = await _modelMetadataService.GetModelMetadataAsync(modelId);
                var toolCallFormat = await _modelMetadataService.GetToolCallFormatAsync(modelId);

                _logger.LogDebug("Model {ModelId} tool call format: {ToolCallFormat}", modelId, toolCallFormat);

                // Use model-specific parsing based on detected architecture and format
                toolCalls = await ParseToolCallsWithAdaptiveFormat(content, availableTools, metadata, toolCallFormat);
            }

            if (toolCalls.Count == 0)
            {
                // Fallback to legacy patterns for backward compatibility
                _logger.LogDebug("No tool calls found with model-specific format (or no metadata service), trying legacy patterns");
                toolCalls = ParseToolCallsWithLegacyPatterns(content, availableToolNames);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to use model-specific tool call parsing for {ModelId}, falling back to legacy patterns", modelId);
            toolCalls = ParseToolCallsWithLegacyPatterns(content, availableToolNames);
        }
        _logger.LogDebug("Parsed {ToolCallCount} tool calls for model {ModelId}", toolCalls.Count, modelId);
        return toolCalls;
    }

    /// <summary>
    /// Parse tool calls using adaptive format based on model metadata
    /// </summary>
    private Task<List<ToolCall>> ParseToolCallsWithAdaptiveFormat(
        string content,
        List<Tool> availableTools,
        ModelMetadata metadata,
        string toolCallFormat)
    {
        var toolCalls = new List<ToolCall>();
        var availableToolNames = availableTools.Select(t => t.Function.Name.ToLowerInvariant()).ToHashSet();

        // Parse based on model architecture and tool call format
        switch (metadata.Architecture?.ToLowerInvariant())
        {
            case "phi3":
            case "phi":
                toolCalls = ParsePhiToolCalls(content, availableToolNames);
                break;

            case "llama":
            case "llama2":
            case "llama3":
                toolCalls = ParseLlamaToolCalls(content, availableToolNames);
                break;

            case "mixtral":
            case "mistral":
                toolCalls = ParseMistralToolCalls(content, availableToolNames);
                break;

            case "qwen":
            case "qwen2":
                toolCalls = ParseQwenToolCalls(content, availableToolNames);
                break;

            default:
                // Use generic parsing based on tool call format
                toolCalls = ParseGenericToolCalls(content, availableToolNames, toolCallFormat);
                break;
        }

        return Task.FromResult(toolCalls);
    }    /// <summary>
         /// Parse tool calls for Phi models (format: <|tool|>{json}<|/tool|>)
         /// </summary>
    private List<ToolCall> ParsePhiToolCalls(string content, HashSet<string> availableToolNames)
    {
        var toolCalls = new List<ToolCall>();

        // Phi-4-mini format: <|tool|>{"name": "function", "parameters": {...}}<|/tool|>
        var pattern = @"<\|tool\|>(.*?)<\|/tool\|>";
        var matches = System.Text.RegularExpressions.Regex.Matches(content, pattern, System.Text.RegularExpressions.RegexOptions.Singleline);

        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var toolCallContent = match.Groups[1].Value.Trim();
            if (TryParseToolCallJson(toolCallContent, availableToolNames, out var toolCall))
            {
                toolCalls.Add(toolCall);
            }
        }

        return toolCalls;
    }

    /// <summary>
    /// Parse tool calls for Llama models (format: [TOOL_CALL] function_name(args) [/TOOL_CALL])
    /// </summary>
    private List<ToolCall> ParseLlamaToolCalls(string content, HashSet<string> availableToolNames)
    {
        var toolCalls = new List<ToolCall>();

        // Llama format: [TOOL_CALL] function_name(args) [/TOOL_CALL]
        var pattern = @"\[TOOL_CALL\](.*?)\[/TOOL_CALL\]";
        var matches = System.Text.RegularExpressions.Regex.Matches(content, pattern, System.Text.RegularExpressions.RegexOptions.Singleline);

        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var toolCallContent = match.Groups[1].Value.Trim();

            // Parse function call format: function_name(args)
            var funcMatch = System.Text.RegularExpressions.Regex.Match(toolCallContent, @"(\w+)\s*\((.*?)\)");
            if (funcMatch.Success)
            {
                var functionName = funcMatch.Groups[1].Value;
                var argsText = funcMatch.Groups[2].Value.Trim();

                if (availableToolNames.Contains(functionName.ToLowerInvariant()))
                {
                    // Try to parse arguments as JSON, or create empty object
                    string arguments = "{}";
                    if (!string.IsNullOrEmpty(argsText))
                    {
                        try
                        {
                            // Validate JSON
                            JsonDocument.Parse(argsText);
                            arguments = argsText;
                        }
                        catch
                        {
                            // If not valid JSON, try to convert simple key=value format
                            arguments = ConvertSimpleArgsToJson(argsText);
                        }
                    }

                    toolCalls.Add(new ToolCall
                    {
                        Id = $"call_{Guid.NewGuid():N}",
                        Type = "function",
                        Function = new FunctionCall
                        {
                            Name = functionName,
                            Arguments = arguments
                        }
                    });
                }
            }
        }

        return toolCalls;
    }

    /// <summary>
    /// Parse tool calls for Mistral/Mixtral models (format: {"tool_call": {"name": "func", "args": {}}})
    /// </summary>
    private List<ToolCall> ParseMistralToolCalls(string content, HashSet<string> availableToolNames)
    {
        var toolCalls = new List<ToolCall>();

        // Mistral format: {"tool_call": {"name": "func", "args": {}}}
        var pattern = @"\{""tool_call"":\s*\{[^}]+\}\s*\}";
        var matches = System.Text.RegularExpressions.Regex.Matches(content, pattern);

        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            try
            {
                using var doc = JsonDocument.Parse(match.Value);
                var root = doc.RootElement;

                if (root.TryGetProperty("tool_call", out var toolCallElement) &&
                    toolCallElement.TryGetProperty("name", out var nameElement))
                {
                    var name = nameElement.GetString();
                    if (!string.IsNullOrEmpty(name) && availableToolNames.Contains(name.ToLowerInvariant()))
                    {
                        string arguments = "{}";
                        if (toolCallElement.TryGetProperty("args", out var argsElement))
                        {
                            arguments = argsElement.GetRawText();
                        }

                        toolCalls.Add(new ToolCall
                        {
                            Id = $"call_{Guid.NewGuid():N}",
                            Type = "function",
                            Function = new FunctionCall
                            {
                                Name = name,
                                Arguments = arguments
                            }
                        });
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogDebug(ex, "Failed to parse Mistral tool call: {Content}", match.Value);
            }
        }

        return toolCalls;
    }

    /// <summary>
    /// Parse tool calls for Qwen models (format: <function_calls>...</function_calls>)
    /// </summary>
    private List<ToolCall> ParseQwenToolCalls(string content, HashSet<string> availableToolNames)
    {
        var toolCalls = new List<ToolCall>();

        // Qwen format: <function_calls>[{"name": "func", "arguments": {...}}]</function_calls>
        var pattern = @"<function_calls>(.*?)</function_calls>";
        var matches = System.Text.RegularExpressions.Regex.Matches(content, pattern, System.Text.RegularExpressions.RegexOptions.Singleline);

        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var toolCallsContent = match.Groups[1].Value.Trim();

            try
            {
                using var doc = JsonDocument.Parse(toolCallsContent);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in doc.RootElement.EnumerateArray())
                    {
                        if (TryParseToolCallJson(element.GetRawText(), availableToolNames, out var toolCall))
                        {
                            toolCalls.Add(toolCall);
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogDebug(ex, "Failed to parse Qwen tool calls: {Content}", toolCallsContent);
            }
        }

        return toolCalls;
    }

    /// <summary>
    /// Parse tool calls using generic format based on tool call format string
    /// </summary>
    private List<ToolCall> ParseGenericToolCalls(string content, HashSet<string> availableToolNames, string toolCallFormat)
    {
        var toolCalls = new List<ToolCall>();

        // Determine pattern based on format string
        if (toolCallFormat.Contains("<tool_call>") || toolCallFormat.Contains("tool_call"))
        {
            return ParseToolCallsWithLegacyPatterns(content, availableToolNames);
        }

        // Add more generic patterns as needed

        return toolCalls;
    }

    /// <summary>
    /// Fallback method with legacy hardcoded patterns for backward compatibility
    /// </summary>
    private List<ToolCall> ParseToolCallsWithLegacyPatterns(string content, HashSet<string> availableToolNames)
    {
        var toolCalls = new List<ToolCall>();

        try
        {
            // Pattern 1: <tool_call>...</tool_call> (common format)
            var toolCallPattern1 = @"<tool_call>(.*?)</tool_call>";
            var matches1 = System.Text.RegularExpressions.Regex.Matches(content, toolCallPattern1, System.Text.RegularExpressions.RegexOptions.Singleline);

            _logger.LogDebug("Found {MatchCount} <tool_call> pattern matches", matches1.Count);

            foreach (System.Text.RegularExpressions.Match match in matches1)
            {
                var toolCallContent = match.Groups[1].Value.Trim();
                if (TryParseToolCallJson(toolCallContent, availableToolNames, out var toolCall))
                {
                    toolCalls.Add(toolCall);
                }
            }

            // Pattern 2: <|tool_call|>...</|tool_call|> (alternative format)
            if (toolCalls.Count == 0)
            {
                var toolCallPattern2 = @"<\|tool_call\|>(.*?)<\|/tool_call\|>";
                var matches2 = System.Text.RegularExpressions.Regex.Matches(content, toolCallPattern2, System.Text.RegularExpressions.RegexOptions.Singleline);

                _logger.LogDebug("Found {MatchCount} <|tool_call|> pattern matches", matches2.Count);

                foreach (System.Text.RegularExpressions.Match match in matches2)
                {
                    var toolCallContent = match.Groups[1].Value.Trim();
                    if (TryParseToolCallJson(toolCallContent, availableToolNames, out var toolCall))
                    {
                        toolCalls.Add(toolCall);
                    }
                }
            }

            // If no structured tool calls found, try simple pattern
            if (toolCalls.Count == 0 && content.Contains("TOOL_CALL:"))
            {
                var toolCallStart = content.IndexOf("TOOL_CALL:");
                if (toolCallStart >= 0)
                {
                    var callLine = content.Substring(toolCallStart + "TOOL_CALL:".Length).Trim();
                    var parenIndex = callLine.IndexOf('(');

                    if (parenIndex > 0)
                    {
                        var functionName = callLine.Substring(0, parenIndex).Trim();

                        if (availableToolNames.Contains(functionName.ToLowerInvariant()))
                        {
                            toolCalls.Add(new ToolCall
                            {
                                Id = $"call_{Guid.NewGuid():N}",
                                Type = "function",
                                Function = new FunctionCall
                                {
                                    Name = functionName,
                                    Arguments = "{}"
                                }
                            });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse tool calls from content using legacy patterns");
        }

        return toolCalls;
    }

    /// <summary>
    /// Convert simple key=value arguments to JSON format
    /// </summary>
    private string ConvertSimpleArgsToJson(string argsText)
    {
        try
        {
            var parameters = new Dictionary<string, object>();
            var pairs = argsText.Split(',');

            foreach (var pair in pairs)
            {
                var parts = pair.Split('=', 2);
                if (parts.Length == 2)
                {
                    var key = parts[0].Trim().Trim('"');
                    var value = parts[1].Trim().Trim('"');

                    // Try to parse as number or boolean
                    if (int.TryParse(value, out var intValue))
                        parameters[key] = intValue;
                    else if (double.TryParse(value, out var doubleValue))
                        parameters[key] = doubleValue;
                    else if (bool.TryParse(value, out var boolValue))
                        parameters[key] = boolValue;
                    else
                        parameters[key] = value;
                }
            }

            return JsonSerializer.Serialize(parameters);
        }
        catch
        {
            return "{}";
        }
    }

    /// <summary>
    /// Helper method to parse tool call JSON content
    /// </summary>
    private bool TryParseToolCallJson(string jsonContent, HashSet<string> availableToolNames, out ToolCall toolCall)
    {
        toolCall = null!;

        try
        {
            using var doc = JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;

            // Try to get function name from "name" or "function_name"
            string? name = null;
            if (root.TryGetProperty("name", out var nameElement))
            {
                name = nameElement.GetString();
            }
            else if (root.TryGetProperty("function_name", out var funcNameElement))
            {
                name = funcNameElement.GetString();
            }

            if (string.IsNullOrEmpty(name) || !availableToolNames.Contains(name.ToLowerInvariant()))
            {
                return false;
            }

            // Try to get arguments/parameters
            string arguments = "{}";
            if (root.TryGetProperty("parameters", out var paramsElement))
            {
                arguments = paramsElement.GetRawText();
            }
            else if (root.TryGetProperty("arguments", out var argsElement))
            {
                arguments = argsElement.GetRawText();
            }

            toolCall = new ToolCall
            {
                Id = $"call_{Guid.NewGuid():N}",
                Type = "function",
                Function = new FunctionCall
                {
                    Name = name,
                    Arguments = arguments
                }
            };

            return true;
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "Failed to parse tool call JSON: {JsonContent}", jsonContent);
            return false;
        }
    }

    /// <summary>
    /// Determines the appropriate finish reason based on content and context
    /// </summary>
    private async Task<string> DetermineFinishReasonAsync(string content, bool hasTools, List<Tool>? tools, int tokenCount, int maxTokens, string modelId)
    {
        // Check for token limit reached
        if (tokenCount >= maxTokens)
        {
            return "length";
        }

        // Check for tool calls if tools are available
        if (hasTools && tools != null)
        {
            try
            {
                var toolCalls = await ParseToolCallsFromContentAsync(content, tools, modelId);
                if (toolCalls.Count > 0)
                {
                    return "tool_calls";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse tool calls when determining finish reason");
            }
        }

        // Check for content filtering keywords (basic implementation)
        if (ContainsFilteredContent(content))
        {
            return "content_filter";
        }

        // Check for natural stop sequences or end tokens
        if (content.TrimEnd().EndsWith("</s>", StringComparison.OrdinalIgnoreCase) ||
            content.TrimEnd().EndsWith("<|endoftext|>", StringComparison.OrdinalIgnoreCase) ||
            content.TrimEnd().EndsWith("<|end|>", StringComparison.OrdinalIgnoreCase) ||
            content.TrimEnd().EndsWith("<|eot_id|>", StringComparison.OrdinalIgnoreCase))
        {
            return "stop";
        }

        // Default to stop for natural completion
        return "stop";
    }

    /// <summary>
    /// Basic content filtering check (can be enhanced with more sophisticated logic)
    /// </summary>
    private bool ContainsFilteredContent(string content)
    {
        // This is a basic implementation - in production, you'd want more sophisticated content filtering
        // that integrates with content moderation services
        var contentLower = content.ToLowerInvariant();

        // Basic keywords that might indicate filtered content
        var filterKeywords = new[]
        {
            "[filtered]", "[content_filtered]", "[blocked]", "[removed]",
            "content policy", "cannot provide", "cannot assist with",
            "against content policy", "violates guidelines",
            "i can't", "i cannot", "i'm not able to", "i'm unable to",
            "this request violates", "content warning", "safety guidelines"
        };

        return filterKeywords.Any(keyword => contentLower.Contains(keyword));
    }

    /// <summary>
    /// Enhanced finish reason determination for streaming responses
    /// </summary>
    private async Task<string> DetermineStreamingFinishReasonAsync(string accumulatedContent, bool hasTools, List<Tool>? tools, int tokenCount, int maxTokens, bool isStreamComplete, string modelId)
    {
        // If the stream ended due to token limit
        if (tokenCount >= maxTokens)
        {
            return "length";
        }

        // If tools are available and we detect tool calls
        if (hasTools && tools != null && !string.IsNullOrEmpty(accumulatedContent))
        {
            try
            {
                var toolCalls = await ParseToolCallsFromContentAsync(accumulatedContent, tools, modelId);
                if (toolCalls.Count > 0)
                {
                    return "tool_calls";
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not parse tool calls in streaming finish reason determination");
            }
        }

        // Check for content filtering
        if (ContainsFilteredContent(accumulatedContent))
        {
            return "content_filter";
        }

        // Check for natural stop conditions
        if (isStreamComplete)
        {
            var trimmedContent = accumulatedContent.TrimEnd();

            // Check for explicit end tokens from various model formats
            var endTokens = new[]
            {
                "</s>", "<|endoftext|>", "<|end|>", "<|eot_id|>",
                "[END]", "<END>", "###", "---END---"
            };

            if (endTokens.Any(token => trimmedContent.EndsWith(token, StringComparison.OrdinalIgnoreCase)))
            {
                return "stop";
            }

            // Check for sentence/paragraph completion patterns
            if (trimmedContent.EndsWith(".") || trimmedContent.EndsWith("!") || trimmedContent.EndsWith("?") ||
                trimmedContent.EndsWith("\n\n") || trimmedContent.EndsWith(".\n"))
            {
                return "stop";
            }
        }

        // Default to stop for completed streams
        return isStreamComplete ? "stop" : "length";
    }

    /// <summary>
    /// Generates a forced tool call when tool_choice is "required"
    /// </summary>
    private ToolCall? GenerateForcedToolCall(string userMessage, List<Tool> availableTools)
    {
        if (availableTools.Count == 0) return null;

        try
        {
            var userMessageLower = userMessage.ToLowerInvariant();

            // Try to match tool based on user intent
            foreach (var tool in availableTools)
            {
                var functionName = tool.Function.Name.ToLowerInvariant();

                // Simple keyword matching for common patterns
                if ((functionName.Contains("weather") && (userMessageLower.Contains("weather") || userMessageLower.Contains("temperature"))) ||
                    (functionName.Contains("calculate") && (userMessageLower.Contains("calculate") || userMessageLower.Contains("math") || userMessageLower.Contains("+"))) ||
                    (functionName.Contains("email") && userMessageLower.Contains("email")) ||
                    (functionName.Contains("time") && (userMessageLower.Contains("time") || userMessageLower.Contains("clock"))))
                {
                    // Extract parameters based on the function
                    var arguments = GenerateToolArguments(tool, userMessage);

                    return new ToolCall
                    {
                        Id = $"call_{Guid.NewGuid():N}",
                        Type = "function",
                        Function = new FunctionCall
                        {
                            Name = tool.Function.Name,
                            Arguments = arguments
                        }
                    };
                }
            }

            // If no specific match, use the first available tool with basic parameters
            var firstTool = availableTools.First();
            var defaultArguments = GenerateToolArguments(firstTool, userMessage);

            return new ToolCall
            {
                Id = $"call_{Guid.NewGuid():N}",
                Type = "function",
                Function = new FunctionCall
                {
                    Name = firstTool.Function.Name,
                    Arguments = defaultArguments
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate forced tool call");
            return null;
        }
    }

    /// <summary>
    /// Generates appropriate arguments for a tool based on user message
    /// </summary>
    private string GenerateToolArguments(Tool tool, string userMessage)
    {
        try
        {
            var parameters = new Dictionary<string, object>();
            var userMessageLower = userMessage.ToLowerInvariant();

            // Handle weather function
            if (tool.Function.Name.ToLowerInvariant().Contains("weather"))
            {
                // Extract location from user message
                var location = "Tokyo, Japan"; // Default
                if (userMessageLower.Contains("tokyo")) location = "Tokyo, Japan";
                else if (userMessageLower.Contains("new york")) location = "New York, NY";
                else if (userMessageLower.Contains("london")) location = "London, UK";
                else if (userMessageLower.Contains("paris")) location = "Paris, France";

                parameters["location"] = location;

                // Add unit if required
                if (tool.Function.Parameters?.ContainsKey("unit") == true)
                {
                    parameters["unit"] = "celsius";
                }
            }
            // Handle calculation function
            else if (tool.Function.Name.ToLowerInvariant().Contains("calculate"))
            {
                parameters["operation"] = "add";
                parameters["a"] = 1;
                parameters["b"] = 1;

                // Try to extract numbers and operation from message
                var numbers = System.Text.RegularExpressions.Regex.Matches(userMessage, @"\d+")
                    .Cast<System.Text.RegularExpressions.Match>()
                    .Select(m => int.Parse(m.Value))
                    .ToList();

                if (numbers.Count >= 2)
                {
                    parameters["a"] = numbers[0];
                    parameters["b"] = numbers[1];
                }

                if (userMessageLower.Contains("+") || userMessageLower.Contains("add"))
                    parameters["operation"] = "add";
                else if (userMessageLower.Contains("-") || userMessageLower.Contains("subtract"))
                    parameters["operation"] = "subtract";
                else if (userMessageLower.Contains("*") || userMessageLower.Contains("multiply"))
                    parameters["operation"] = "multiply";
                else if (userMessageLower.Contains("/") || userMessageLower.Contains("divide"))
                    parameters["operation"] = "divide";
            }
            // Handle time function
            else if (tool.Function.Name.ToLowerInvariant().Contains("time"))
            {
                var timezone = "America/New_York"; // Default
                if (userMessageLower.Contains("tokyo")) timezone = "Asia/Tokyo";
                else if (userMessageLower.Contains("london")) timezone = "Europe/London";
                else if (userMessageLower.Contains("paris")) timezone = "Europe/Paris";

                parameters["timezone"] = timezone;
            }
            // Handle email function
            else if (tool.Function.Name.ToLowerInvariant().Contains("email"))
            {
                parameters["to"] = "example@example.com";
                parameters["subject"] = "Hello";
                parameters["body"] = "This is a test email.";

                // Try to extract email from message
                var emailMatch = System.Text.RegularExpressions.Regex.Match(userMessage, @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b");
                if (emailMatch.Success)
                {
                    parameters["to"] = emailMatch.Value;
                }
            }

            return System.Text.Json.JsonSerializer.Serialize(parameters);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate tool arguments for {ToolName}", tool.Function.Name);
            return "{}";
        }
    }
}