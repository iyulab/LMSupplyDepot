using LMSupplyDepots.SDK.OpenAI.Models;
using LMSupplyDepots.SDK.Tools;
using Microsoft.Extensions.Logging;

namespace LMSupplyDepots.Host.Services;

/// <summary>
/// Service for handling OpenAI-compatible tool execution with proper conversation flow
/// </summary>
public interface IToolExecutionService
{
    /// <summary>
    /// Execute tool calls and return updated conversation messages
    /// </summary>
    Task<List<OpenAIChatMessage>> ExecuteToolCallsAsync(
        List<ToolCall> toolCalls,
        List<OpenAIChatMessage> conversationHistory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate tool calls against available tools
    /// </summary>
    bool ValidateToolCalls(List<ToolCall> toolCalls, List<Tool> availableTools);

    /// <summary>
    /// Generate follow-up response after tool execution
    /// </summary>
    Task<OpenAIChatCompletionResponse> GenerateFollowUpResponseAsync(
        OpenAIChatCompletionRequest originalRequest,
        List<OpenAIChatMessage> updatedMessages,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementation of tool execution service with OpenAI compatibility
/// </summary>
public class ToolExecutionService : IToolExecutionService
{
    private readonly IToolService _toolService;
    private readonly ILogger<ToolExecutionService> _logger;

    public ToolExecutionService(IToolService toolService, ILogger<ToolExecutionService> logger)
    {
        _toolService = toolService;
        _logger = logger;
    }

    public async Task<List<OpenAIChatMessage>> ExecuteToolCallsAsync(
        List<ToolCall> toolCalls,
        List<OpenAIChatMessage> conversationHistory,
        CancellationToken cancellationToken = default)
    {
        var updatedMessages = new List<OpenAIChatMessage>(conversationHistory);

        // Add assistant message with tool calls
        var assistantMessage = new OpenAIChatMessage
        {
            Role = "assistant",
            ToolCalls = toolCalls,
            Content = null // No content when tool_calls are present
        };
        updatedMessages.Add(assistantMessage);

        // Execute each tool call and add tool messages
        foreach (var toolCall in toolCalls)
        {
            try
            {
                _logger.LogInformation("Executing tool call: {ToolName} with ID: {CallId}",
                    toolCall.Function.Name, toolCall.Id);

                var toolResult = await _toolService.ExecuteToolAsync(
                    toolCall.Function.Name,
                    toolCall.Function.Arguments,
                    cancellationToken);

                // Add tool response message
                var toolMessage = new OpenAIChatMessage
                {
                    Role = "tool",
                    ToolCallId = toolCall.Id,
                    Content = new TextContentPart { Text = toolResult }
                };
                updatedMessages.Add(toolMessage);

                _logger.LogInformation("Tool call {CallId} executed successfully", toolCall.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute tool call {CallId}: {ToolName}",
                    toolCall.Id, toolCall.Function.Name);

                // Add error response as tool message
                var errorMessage = new OpenAIChatMessage
                {
                    Role = "tool",
                    ToolCallId = toolCall.Id,
                    Content = new TextContentPart { Text = $"Error executing tool: {ex.Message}" }
                };
                updatedMessages.Add(errorMessage);
            }
        }

        return updatedMessages;
    }

    public bool ValidateToolCalls(List<ToolCall> toolCalls, List<Tool> availableTools)
    {
        var availableToolNames = availableTools.Select(t => t.Function.Name.ToLowerInvariant()).ToHashSet();

        foreach (var toolCall in toolCalls)
        {
            // Validate tool call structure
            if (string.IsNullOrEmpty(toolCall.Id) || !toolCall.Id.StartsWith("call_"))
            {
                _logger.LogWarning("Invalid tool call ID: {Id}", toolCall.Id);
                return false;
            }

            if (toolCall.Function == null || string.IsNullOrEmpty(toolCall.Function.Name))
            {
                _logger.LogWarning("Tool call missing function name: {Id}", toolCall.Id);
                return false;
            }

            // Validate tool is available
            if (!availableToolNames.Contains(toolCall.Function.Name.ToLowerInvariant()))
            {
                _logger.LogWarning("Tool call references unavailable function: {FunctionName}", toolCall.Function.Name);
                return false;
            }

            // Validate arguments are valid JSON
            if (!string.IsNullOrEmpty(toolCall.Function.Arguments))
            {
                try
                {
                    System.Text.Json.JsonDocument.Parse(toolCall.Function.Arguments);
                }
                catch (System.Text.Json.JsonException)
                {
                    _logger.LogWarning("Tool call has invalid JSON arguments: {Id}", toolCall.Id);
                    return false;
                }
            }
        }

        return true;
    }

    public Task<OpenAIChatCompletionResponse> GenerateFollowUpResponseAsync(
        OpenAIChatCompletionRequest originalRequest,
        List<OpenAIChatMessage> updatedMessages,
        CancellationToken cancellationToken = default)
    {
        // Create a new request with updated messages but without tools to prevent recursive tool calling
        var followUpRequest = new OpenAIChatCompletionRequest
        {
            Model = originalRequest.Model,
            Messages = updatedMessages,
            MaxCompletionTokens = originalRequest.MaxCompletionTokens,
            Temperature = originalRequest.Temperature,
            TopP = originalRequest.TopP,
            Stream = false, // Always non-streaming for follow-up
            // Deliberately not including Tools to prevent recursive calls
        };

        // Note: This would need to be called through the main LMSupplyDepot service
        // For now, we'll return a placeholder response
        // In practice, this should call back to the main completion service

        var completionId = $"chatcmpl-{Guid.NewGuid():N}";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var response = new OpenAIChatCompletionResponse
        {
            Id = completionId,
            Created = timestamp,
            Model = originalRequest.Model,
            Choices = new List<OpenAIChatChoice>
            {
                new OpenAIChatChoice
                {
                    Index = 0,
                    Message = new OpenAIChatMessage
                    {
                        Role = "assistant",
                        Content = new TextContentPart { Text = "Tool execution completed. Follow-up generation would be handled by the main service." }
                    },
                    FinishReason = "stop"
                }
            },
            Usage = new OpenAIUsage
            {
                PromptTokens = 0,
                CompletionTokens = 0,
                TotalTokens = 0
            }
        };

        return Task.FromResult(response);
    }
}
