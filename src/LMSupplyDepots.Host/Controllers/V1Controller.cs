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
    private readonly IDynamicToolService _dynamicToolService;
    private readonly IModelMetadataService? _modelMetadataService;
    private readonly IReasoningService _reasoningService;
    private readonly ILogger<V1Controller> _logger;

    /// <summary>
    /// Initializes a new instance of the V1Controller
    /// </summary>
    public V1Controller(
        IHostService hostService,
        IToolExecutionService toolExecutionService,
        IDynamicToolService dynamicToolService,
        IReasoningService reasoningService,
        ILogger<V1Controller> logger,
        IServiceProvider serviceProvider)
    {
        _hostService = hostService;
        _toolExecutionService = toolExecutionService;
        _dynamicToolService = dynamicToolService;
        _reasoningService = reasoningService;
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

            // Apply dynamic tool formatting if tools are present
            if (request.Tools != null && request.Tools.Count > 0)
            {
                await _dynamicToolService.ApplyToolFormattingAsync(request, request.Model, cancellationToken);
            }

            // Use HostService OpenAI method for initial completion
            var response = await _hostService.CreateChatCompletionAsync(request, cancellationToken);

            // Cast to get the actual response type for tool call processing
            var chatResponse = response as OpenAIChatCompletionResponse;
            if (chatResponse?.Choices?.FirstOrDefault()?.Message != null)
            {
                var message = chatResponse.Choices.First().Message;

                // Process reasoning content if present
                var messageContent = ExtractContentText(message.Content);
                if (!string.IsNullOrEmpty(messageContent))
                {
                    var reasoningResult = await _reasoningService.ProcessReasoningAsync(messageContent, cancellationToken);

                    if (reasoningResult.HasReasoning)
                    {
                        _logger.LogDebug("Reasoning content detected: thinking={ThinkingLength}, answer={AnswerLength}, tokens={ReasoningTokens}",
                            reasoningResult.ThinkingContent.Length, reasoningResult.FinalAnswer.Length, reasoningResult.ReasoningTokens);

                        // Update message content to only include the final answer
                        if (message.Content is TextContentPart textContent)
                        {
                            textContent.Text = reasoningResult.FinalAnswer;
                        }

                        // Update usage to include reasoning tokens
                        if (chatResponse.Usage != null)
                        {
                            chatResponse.Usage.ReasoningTokens = reasoningResult.ReasoningTokens;
                            chatResponse.Usage.TotalTokens += reasoningResult.ReasoningTokens;
                        }
                    }
                }

                // Handle tool_choice "required" by generating appropriate tool call
                if (request.Tools != null && request.Tools.Count > 0 &&
                    request.ToolChoice?.Type == "required")
                {
                    _logger.LogInformation("Tool choice is 'required', generating dynamic tool call");

                    var userMessage = ExtractUserMessage(request.Messages);
                    var forcedToolCall = await GenerateDynamicToolCallAsync(userMessage, request.Tools, request.Model, cancellationToken);
                    
                    if (forcedToolCall != null)
                    {
                        _logger.LogInformation("Generated dynamic tool call: {ToolCallId} - {FunctionName}",
                            forcedToolCall.Id, forcedToolCall.Function.Name);

                        return Ok(new OpenAIChatCompletionResponse
                        {
                            Id = chatResponse.Id,
                            Object = chatResponse.Object,
                            Created = chatResponse.Created,
                            Model = chatResponse.Model,
                            Usage = chatResponse.Usage!,
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

                // Check for tool calls in model response using dynamic parsing
                string? contentText = ExtractContentText(message.Content);
                
                if (request.Tools != null && request.Tools.Count > 0 && !string.IsNullOrEmpty(contentText))
                {
                    _logger.LogInformation("Checking for tool calls using dynamic parsing. Tools available: {ToolCount}, Content length: {ContentLength}",
                        request.Tools.Count, contentText.Length);

                    try
                    {
                        // Use dynamic tool service for parsing
                        var toolCalls = await _dynamicToolService.ParseToolCallsAsync(contentText, request.Tools, request.Model, cancellationToken);

                        _logger.LogInformation("Dynamic tool parsing completed. Found {ToolCallCount} tool calls", toolCalls.Count);

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
                                Usage = chatResponse.Usage!,
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
                        _logger.LogWarning(toolEx, "Dynamic tool call parsing failed, returning original response. Content: {ContentPreview}",
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
            // Apply dynamic tool formatting if tools are present
            if (request.Tools != null && request.Tools.Count > 0)
            {
                await _dynamicToolService.ApplyToolFormattingAsync(request, request.Model, cancellationToken);
            }

            // Set streaming response headers
            Response.ContentType = "text/event-stream";
            Response.Headers["Cache-Control"] = "no-cache";
            Response.Headers["Connection"] = "keep-alive";

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

            // Process reasoning content in accumulated response
            var fullContent = accumulatedContent.ToString();
            var reasoningResult = await _reasoningService.ProcessReasoningAsync(fullContent, cancellationToken);

            if (reasoningResult.HasReasoning)
            {
                _logger.LogDebug("Streaming reasoning content detected: thinking={ThinkingLength}, answer={AnswerLength}, tokens={ReasoningTokens}",
                    reasoningResult.ThinkingContent.Length, reasoningResult.FinalAnswer.Length, reasoningResult.ReasoningTokens);

                // Send corrected content chunk with final answer only
                var correctedResponse = new
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
                            delta = new { content = reasoningResult.FinalAnswer },
                            finish_reason = (string?)null
                        }
                    }
                };

                var correctedJson = JsonSerializer.Serialize(correctedResponse, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                await Response.WriteAsync($"data: {correctedJson}\n\n");
                await Response.Body.FlushAsync(cancellationToken);

                // Update accumulated content to final answer for tool parsing
                fullContent = reasoningResult.FinalAnswer;
            }

            // After streaming is complete, check for tool calls if tools are available
            string finishReason = await DetermineStreamingFinishReasonAsync(
                fullContent,
                hasTools,
                request.Tools,
                fullContent.Length,
                request.MaxCompletionTokens ?? 2048,
                true,
                request.Model,
                cancellationToken);
            object? finalDelta = new { };

            if (hasTools)
            {
                var toolCalls = await _dynamicToolService.ParseToolCallsAsync(fullContent, request.Tools!, request.Model, cancellationToken);

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
    /// Generate dynamic tool call using schema-based parameter extraction
    /// </summary>
    private async Task<ToolCall?> GenerateDynamicToolCallAsync(
        string userMessage, 
        List<Tool> availableTools, 
        string modelId,
        CancellationToken cancellationToken)
    {
        if (availableTools.Count == 0) return null;

        try
        {
            // Find the most relevant tool based on user message
            var selectedTool = SelectMostRelevantTool(userMessage, availableTools);
            
            // Generate arguments using dynamic extraction
            var arguments = await _dynamicToolService.GenerateToolArgumentsAsync(selectedTool, userMessage, modelId, cancellationToken);

            return new ToolCall
            {
                Id = $"call_{Guid.NewGuid():N}",
                Type = "function",
                Function = new FunctionCall
                {
                    Name = selectedTool.Function.Name,
                    Arguments = arguments
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate dynamic tool call");
            return null;
        }
    }

    /// <summary>
    /// Select most relevant tool based on user message
    /// </summary>
    private Tool SelectMostRelevantTool(string userMessage, List<Tool> availableTools)
    {
        var messageLower = userMessage.ToLowerInvariant();

        // Score each tool based on keyword matching
        var scoredTools = availableTools.Select(tool => new
        {
            Tool = tool,
            Score = CalculateToolRelevanceScore(messageLower, tool)
        }).OrderByDescending(x => x.Score).ToList();

        return scoredTools.First().Tool;
    }

    /// <summary>
    /// Calculate relevance score for a tool based on user message
    /// </summary>
    private int CalculateToolRelevanceScore(string messageLower, Tool tool)
    {
        var score = 0;
        var functionName = tool.Function.Name.ToLowerInvariant();
        var description = tool.Function.Description?.ToLowerInvariant() ?? "";

        // Function name matching
        if (messageLower.Contains(functionName))
            score += 10;

        // Description keyword matching
        var descriptionWords = description.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in descriptionWords)
        {
            if (messageLower.Contains(word))
                score += 2;
        }

        // Common function type matching
        if (functionName.Contains("calculate") && (messageLower.Contains("calculate") || messageLower.Contains("math") || messageLower.Contains("+")))
            score += 15;
        if (functionName.Contains("weather") && (messageLower.Contains("weather") || messageLower.Contains("temperature")))
            score += 15;
        if (functionName.Contains("time") && (messageLower.Contains("time") || messageLower.Contains("clock")))
            score += 15;
        if (functionName.Contains("email") && messageLower.Contains("email"))
            score += 15;

        return score;
    }

    /// <summary>
    /// Extract user message from messages list
    /// </summary>
    private string ExtractUserMessage(List<OpenAIChatMessage> messages)
    {
        var userMessage = messages.LastOrDefault(m => m.Role.ToLowerInvariant() == "user");
        return ExtractContentText(userMessage?.Content) ?? "";
    }

    /// <summary>
    /// Extract text content from ContentPart
    /// </summary>
    private string? ExtractContentText(ContentPart? content)
    {
        return content switch
        {
            TextContentPart textPart => textPart.Text,
            _ => content?.ToString()
        };
    }

    /// <summary>
    /// Enhanced finish reason determination for streaming responses using dynamic parsing
    /// </summary>
    private async Task<string> DetermineStreamingFinishReasonAsync(
        string accumulatedContent, 
        bool hasTools, 
        List<Tool>? tools, 
        int tokenCount, 
        int maxTokens, 
        bool isStreamComplete, 
        string modelId,
        CancellationToken cancellationToken)
    {
        // If the stream ended due to token limit
        if (tokenCount >= maxTokens)
        {
            return "length";
        }

        // If tools are available and we detect tool calls using dynamic parsing
        if (hasTools && tools != null && !string.IsNullOrEmpty(accumulatedContent))
        {
            try
            {
                var toolCalls = await _dynamicToolService.ParseToolCallsAsync(accumulatedContent, tools, modelId, cancellationToken);
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
    /// Basic content filtering check
    /// </summary>
    private bool ContainsFilteredContent(string content)
    {
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
}