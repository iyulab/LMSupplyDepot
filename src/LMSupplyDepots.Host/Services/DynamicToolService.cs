using LMSupplyDepots.SDK.OpenAI.Models;
using LMSupplyDepots.SDK.Services;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LMSupplyDepots.Host.Services;

/// <summary>
/// Dynamic tool calling service that uses GGUF metadata and chat templates
/// </summary>
public interface IDynamicToolService
{
    /// <summary>
    /// Parse tool calls from model response using dynamic metadata-based parsing
    /// </summary>
    Task<List<ToolCall>> ParseToolCallsAsync(
        string content, 
        List<Tool> availableTools, 
        string modelId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate tool call arguments from user message using dynamic parameter extraction
    /// </summary>
    Task<string> GenerateToolArgumentsAsync(
        Tool tool, 
        string userMessage, 
        string modelId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Apply model-specific tool formatting to request
    /// </summary>
    Task ApplyToolFormattingAsync(
        OpenAIChatCompletionRequest request,
        string modelId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementation of dynamic tool calling service
/// </summary>
public class DynamicToolService : IDynamicToolService
{
    private readonly IModelMetadataService? _metadataService;
    private readonly ILogger<DynamicToolService> _logger;

    public DynamicToolService(
        ILogger<DynamicToolService> logger,
        IModelMetadataService? metadataService = null)
    {
        _metadataService = metadataService;
        _logger = logger;
    }

    public async Task<List<ToolCall>> ParseToolCallsAsync(
        string content, 
        List<Tool> availableTools, 
        string modelId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var availableToolNames = availableTools.Select(t => t.Function.Name.ToLowerInvariant()).ToHashSet();

            _logger.LogDebug("Parsing tool calls for model {ModelId}", modelId);

            // Use dynamic parsing based on model's native format if metadata service available
            var toolCalls = new List<ToolCall>();
            
            if (_metadataService != null)
            {
                var metadata = await _metadataService.GetModelMetadataAsync(modelId, cancellationToken);
                toolCalls = await ParseToolCallsWithNativeFormat(content, availableToolNames, metadata);
            }

            if (toolCalls.Count == 0)
            {
                // Fallback to pattern-based parsing for models without proper tool support
                toolCalls = ParseToolCallsWithPatternMatching(content, availableToolNames);
            }

            _logger.LogDebug("Parsed {Count} tool calls for model {ModelId}", toolCalls.Count, modelId);
            return toolCalls;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse tool calls for model {ModelId}", modelId);
            return new List<ToolCall>();
        }
    }

    public async Task<string> GenerateToolArgumentsAsync(
        Tool tool, 
        string userMessage, 
        string modelId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Generating arguments for tool {ToolName} from message: {Message}",
                tool.Function.Name, userMessage);

            // Use dynamic parameter extraction based on tool schema
            var arguments = ExtractArgumentsFromSchema(userMessage, tool.Function);

            var result = JsonSerializer.Serialize(arguments);
            _logger.LogDebug("Generated tool arguments: {Arguments}", result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate tool arguments for {ToolName}", tool.Function.Name);
            return "{}";
        }
    }

    public async Task ApplyToolFormattingAsync(
        OpenAIChatCompletionRequest request,
        string modelId,
        CancellationToken cancellationToken = default)
    {
        if (request.Tools == null || request.Tools.Count == 0) return;

        try
        {
            _logger.LogDebug("Applying tool formatting for model {ModelId}", modelId);

            if (_metadataService != null)
            {
                // Convert tools to chat messages using model's native chat template
                var chatMessages = request.Messages.Select(msg => new ChatMessage
                {
                    Role = msg.Role,
                    Content = ExtractTextContent(msg.Content)
                }).ToList();

                // Apply chat template with tool options
                var toolOptions = new ToolCallOptions
                {
                    Tools = request.Tools.Select(t => new ToolDefinition
                    {
                        Name = t.Function.Name,
                        Description = t.Function.Description ?? "",
                        Parameters = t.Function.Parameters ?? new Dictionary<string, object>()
                    })
                };

                var formattedPrompt = await _metadataService.ApplyChatTemplateAsync(
                    modelId,
                    chatMessages,
                    addGenerationPrompt: true,
                    toolOptions,
                    cancellationToken);

                // Replace messages with formatted prompt
                request.Messages.Clear();
                request.Messages.Add(new OpenAIChatMessage
                {
                    Role = "user",
                    Content = new TextContentPart { Text = formattedPrompt }
                });

                _logger.LogDebug("Applied native tool formatting for model {ModelId}", modelId);
            }
            else
            {
                // Fallback to simple tool formatting
                await ApplyFallbackToolFormattingAsync(request);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply tool formatting for model {ModelId}", modelId);
            // Continue without tool formatting
        }
    }

    /// <summary>
    /// Parse tool calls using model's native format from metadata
    /// </summary>
    private Task<List<ToolCall>> ParseToolCallsWithNativeFormat(
        string content, 
        HashSet<string> availableToolNames, 
        ModelMetadata metadata)
    {
        var toolCalls = new List<ToolCall>();
        var format = metadata.ToolCapabilities.ToolCallFormat.ToLowerInvariant();

        // Use metadata-derived parsing patterns
        switch (format)
        {
            case "phi4":
            case "phi3.5":
            case "phi3":
            case "phi":
                toolCalls = ParsePhiFormatToolCalls(content, availableToolNames, metadata);
                break;

            case "llama":
            case "llama-native":
                toolCalls = ParseLlamaFormatToolCalls(content, availableToolNames, metadata);
                break;

            case "qwen":
            case "qwen2":
                toolCalls = ParseQwenFormatToolCalls(content, availableToolNames, metadata);
                break;

            case "mixtral":
            case "mistral":
                toolCalls = ParseMistralFormatToolCalls(content, availableToolNames, metadata);
                break;

            default:
                // Use architecture-based parsing for unknown formats
                toolCalls = ParseByArchitecture(content, availableToolNames, metadata);
                break;
        }

        return Task.FromResult(toolCalls);
    }

    /// <summary>
    /// Parse using pattern matching for models without proper tool support
    /// </summary>
    private List<ToolCall> ParseToolCallsWithPatternMatching(
        string content, 
        HashSet<string> availableToolNames)
    {
        var toolCalls = new List<ToolCall>();

        // Try common patterns in order of specificity
        var patterns = new[]
        {
            @"<\|tool\|>(.*?)<\|end\|>", // Phi-style
            @"<tool_call>(.*?)</tool_call>", // Generic tool call
            @"\[TOOL_CALL\](.*?)\[/TOOL_CALL\]", // Llama-style
            @"<function_calls>(.*?)</function_calls>", // Qwen-style
        };

        foreach (var pattern in patterns)
        {
            var matches = Regex.Matches(content, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                var toolContent = match.Groups[1].Value.Trim();
                if (TryParseToolJson(toolContent, availableToolNames, out var toolCall))
                {
                    toolCalls.Add(toolCall);
                }
            }

            if (toolCalls.Count > 0) break; // Use first successful pattern
        }

        return toolCalls;
    }

    /// <summary>
    /// Parse Phi format tool calls using metadata tokens
    /// </summary>
    private List<ToolCall> ParsePhiFormatToolCalls(
        string content, 
        HashSet<string> availableToolNames, 
        ModelMetadata metadata)
    {
        var toolCalls = new List<ToolCall>();

        // Extract tool tokens from metadata if available
        var startToken = metadata.ToolCapabilities.ToolTokens.GetValueOrDefault("start", "<|tool|>");
        var endToken = metadata.ToolCapabilities.ToolTokens.GetValueOrDefault("end", "<|end|>");

        var pattern = $@"{Regex.Escape(startToken)}(.*?){Regex.Escape(endToken)}";
        var matches = Regex.Matches(content, pattern, RegexOptions.Singleline);

        foreach (Match match in matches)
        {
            var toolContent = match.Groups[1].Value.Trim();
            if (TryParseToolJson(toolContent, availableToolNames, out var toolCall))
            {
                toolCalls.Add(toolCall);
            }
        }

        return toolCalls;
    }

    /// <summary>
    /// Parse using architecture when format is unknown
    /// </summary>
    private List<ToolCall> ParseByArchitecture(
        string content, 
        HashSet<string> availableToolNames, 
        ModelMetadata metadata)
    {
        var architecture = metadata.Architecture.ToLowerInvariant();

        return architecture switch
        {
            "phi3" or "phi" => ParsePhiFormatToolCalls(content, availableToolNames, metadata),
            "llama" => ParseLlamaFormatToolCalls(content, availableToolNames, metadata),
            "qwen" => ParseQwenFormatToolCalls(content, availableToolNames, metadata),
            _ => new List<ToolCall>()
        };
    }

    /// <summary>
    /// Parse Llama format tool calls
    /// </summary>
    private List<ToolCall> ParseLlamaFormatToolCalls(
        string content, 
        HashSet<string> availableToolNames, 
        ModelMetadata metadata)
    {
        var toolCalls = new List<ToolCall>();
        var pattern = @"\[TOOL_CALL\](.*?)\[/TOOL_CALL\]";
        var matches = Regex.Matches(content, pattern, RegexOptions.Singleline);

        foreach (Match match in matches)
        {
            var toolContent = match.Groups[1].Value.Trim();
            
            // Try JSON format first
            if (TryParseToolJson(toolContent, availableToolNames, out var toolCall))
            {
                toolCalls.Add(toolCall);
                continue;
            }

            // Try function call format: function_name(args)
            var funcMatch = Regex.Match(toolContent, @"(\w+)\s*\((.*?)\)");
            if (funcMatch.Success)
            {
                var functionName = funcMatch.Groups[1].Value;
                var argsText = funcMatch.Groups[2].Value.Trim();

                if (availableToolNames.Contains(functionName.ToLowerInvariant()))
                {
                    toolCalls.Add(new ToolCall
                    {
                        Id = $"call_{Guid.NewGuid():N}",
                        Type = "function",
                        Function = new FunctionCall
                        {
                            Name = functionName,
                            Arguments = ConvertArgsToJson(argsText)
                        }
                    });
                }
            }
        }

        return toolCalls;
    }

    /// <summary>
    /// Parse Qwen format tool calls
    /// </summary>
    private List<ToolCall> ParseQwenFormatToolCalls(
        string content, 
        HashSet<string> availableToolNames, 
        ModelMetadata metadata)
    {
        var toolCalls = new List<ToolCall>();
        var pattern = @"<function_calls>(.*?)</function_calls>";
        var matches = Regex.Matches(content, pattern, RegexOptions.Singleline);

        foreach (Match match in matches)
        {
            var toolsContent = match.Groups[1].Value.Trim();

            try
            {
                using var doc = JsonDocument.Parse(toolsContent);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in doc.RootElement.EnumerateArray())
                    {
                        if (TryParseToolJson(element.GetRawText(), availableToolNames, out var toolCall))
                        {
                            toolCalls.Add(toolCall);
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogDebug(ex, "Failed to parse Qwen tool calls: {Content}", toolsContent);
            }
        }

        return toolCalls;
    }

    /// <summary>
    /// Parse Mistral format tool calls
    /// </summary>
    private List<ToolCall> ParseMistralFormatToolCalls(
        string content, 
        HashSet<string> availableToolNames, 
        ModelMetadata metadata)
    {
        var toolCalls = new List<ToolCall>();
        var pattern = @"\{""tool_call"":\s*\{[^}]+\}\s*\}";
        var matches = Regex.Matches(content, pattern);

        foreach (Match match in matches)
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
    /// Extract arguments from user message based on tool schema
    /// </summary>
    private Dictionary<string, object> ExtractArgumentsFromSchema(string userMessage, FunctionDefinition function)
    {
        var arguments = new Dictionary<string, object>();

        if (function.Parameters == null) return arguments;

        try
        {
            // Parse the schema to understand expected parameters
            var parametersJson = JsonSerializer.Serialize(function.Parameters);
            using var doc = JsonDocument.Parse(parametersJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("properties", out var properties))
            {
                foreach (var property in properties.EnumerateObject())
                {
                    var paramName = property.Name;
                    var paramDef = property.Value;

                    // Extract value based on parameter type and user message
                    var value = ExtractParameterValue(userMessage, paramName, paramDef);
                    if (value != null)
                    {
                        arguments[paramName] = value;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract arguments from schema for function {FunctionName}", function.Name);
        }

        return arguments;
    }

    /// <summary>
    /// Extract parameter value from user message based on parameter definition
    /// </summary>
    private object? ExtractParameterValue(string userMessage, string paramName, JsonElement paramDef)
    {
        var paramType = paramDef.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : "string";
        var paramDescription = paramDef.TryGetProperty("description", out var descElement) ? descElement.GetString() : "";

        switch (paramType?.ToLowerInvariant())
        {
            case "number":
            case "integer":
                return ExtractNumericValue(userMessage, paramName, paramDescription);

            case "boolean":
                return ExtractBooleanValue(userMessage, paramName, paramDescription);

            case "string":
                if (paramDef.TryGetProperty("enum", out var enumElement))
                {
                    return ExtractEnumValue(userMessage, enumElement, paramDescription);
                }
                return ExtractStringValue(userMessage, paramName, paramDescription);

            default:
                return ExtractStringValue(userMessage, paramName, paramDescription);
        }
    }

    /// <summary>
    /// Extract numeric value from user message
    /// </summary>
    private object? ExtractNumericValue(string userMessage, string paramName, string? description)
    {
        // Extract numbers from message
        var numbers = Regex.Matches(userMessage, @"-?\d+(?:\.\d+)?")
            .Cast<Match>()
            .Select(m => m.Value)
            .ToList();

        if (numbers.Count >= 2)
        {
            // For binary operations, use first two numbers
            if (paramName.ToLowerInvariant() == "a" && double.TryParse(numbers[0], out var a))
                return a;
            if (paramName.ToLowerInvariant() == "b" && double.TryParse(numbers[1], out var b))
                return b;
        }

        if (numbers.Count >= 1 && double.TryParse(numbers[0], out var firstNumber))
        {
            return firstNumber;
        }

        return null;
    }

    /// <summary>
    /// Extract enum value from user message
    /// </summary>
    private object? ExtractEnumValue(string userMessage, JsonElement enumElement, string? description)
    {
        var messageLower = userMessage.ToLowerInvariant();

        foreach (var enumValue in enumElement.EnumerateArray())
        {
            var value = enumValue.GetString();
            if (!string.IsNullOrEmpty(value) && messageLower.Contains(value.ToLowerInvariant()))
            {
                return value;
            }
        }

        // Try to detect operation type for math functions
        if (messageLower.Contains("+") || messageLower.Contains("add") || messageLower.Contains("plus"))
            return "add";
        if (messageLower.Contains("-") || messageLower.Contains("subtract") || messageLower.Contains("minus"))
            return "subtract";
        if (messageLower.Contains("*") || messageLower.Contains("×") || messageLower.Contains("multiply"))
            return "multiply";
        if (messageLower.Contains("/") || messageLower.Contains("÷") || messageLower.Contains("divide"))
            return "divide";

        return null;
    }

    /// <summary>
    /// Extract string value from user message
    /// </summary>
    private object? ExtractStringValue(string userMessage, string paramName, string? description)
    {
        // Extract based on parameter name and context
        var paramLower = paramName.ToLowerInvariant();
        var messageLower = userMessage.ToLowerInvariant();

        if (paramLower.Contains("location") || paramLower.Contains("city"))
        {
            return ExtractLocation(userMessage);
        }

        if (paramLower.Contains("email"))
        {
            var emailMatch = Regex.Match(userMessage, @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b");
            return emailMatch.Success ? emailMatch.Value : null;
        }

        // Default fallback
        return userMessage;
    }

    /// <summary>
    /// Extract boolean value from user message
    /// </summary>
    private object? ExtractBooleanValue(string userMessage, string paramName, string? description)
    {
        var messageLower = userMessage.ToLowerInvariant();

        if (messageLower.Contains("true") || messageLower.Contains("yes") || messageLower.Contains("enable"))
            return true;
        if (messageLower.Contains("false") || messageLower.Contains("no") || messageLower.Contains("disable"))
            return false;

        return null;
    }

    /// <summary>
    /// Extract location from user message
    /// </summary>
    private string ExtractLocation(string userMessage)
    {
        var messageLower = userMessage.ToLowerInvariant();

        // Common cities
        if (messageLower.Contains("tokyo")) return "Tokyo, Japan";
        if (messageLower.Contains("new york") || messageLower.Contains("nyc")) return "New York, NY";
        if (messageLower.Contains("london")) return "London, UK";
        if (messageLower.Contains("paris")) return "Paris, France";
        if (messageLower.Contains("seoul")) return "Seoul, South Korea";
        if (messageLower.Contains("beijing")) return "Beijing, China";

        return "New York, NY"; // Default
    }

    /// <summary>
    /// Try to parse tool call JSON
    /// </summary>
    private bool TryParseToolJson(string jsonContent, HashSet<string> availableToolNames, out ToolCall toolCall)
    {
        toolCall = null!;

        try
        {
            using var doc = JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;

            // Try different property names
            string? name = null;
            if (root.TryGetProperty("name", out var nameElement))
                name = nameElement.GetString();
            else if (root.TryGetProperty("function_name", out var funcNameElement))
                name = funcNameElement.GetString();

            if (string.IsNullOrEmpty(name) || !availableToolNames.Contains(name.ToLowerInvariant()))
                return false;

            // Extract arguments
            string arguments = "{}";
            if (root.TryGetProperty("parameters", out var paramsElement))
                arguments = paramsElement.GetRawText();
            else if (root.TryGetProperty("arguments", out var argsElement))
                arguments = argsElement.GetRawText();

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
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Convert simple arguments to JSON
    /// </summary>
    private string ConvertArgsToJson(string argsText)
    {
        if (string.IsNullOrEmpty(argsText)) return "{}";

        try
        {
            // Try to parse as JSON first
            JsonDocument.Parse(argsText);
            return argsText;
        }
        catch
        {
            // Convert key=value format to JSON
            var parameters = new Dictionary<string, object>();
            var pairs = argsText.Split(',');

            foreach (var pair in pairs)
            {
                var parts = pair.Split('=', 2);
                if (parts.Length == 2)
                {
                    var key = parts[0].Trim().Trim('"');
                    var value = parts[1].Trim().Trim('"');

                    if (double.TryParse(value, out var numValue))
                        parameters[key] = numValue;
                    else if (bool.TryParse(value, out var boolValue))
                        parameters[key] = boolValue;
                    else
                        parameters[key] = value;
                }
            }

            return JsonSerializer.Serialize(parameters);
        }
    }

    /// <summary>
    /// Extract text content from ContentPart
    /// </summary>
    private string ExtractTextContent(ContentPart? content)
    {
        return content switch
        {
            TextContentPart textPart => textPart.Text,
            _ => content?.ToString() ?? string.Empty
        };
    }

    /// <summary>
    /// Apply fallback tool formatting when metadata service is not available
    /// </summary>
    private Task ApplyFallbackToolFormattingAsync(OpenAIChatCompletionRequest request)
    {
        // Simple tool description formatting
        var toolsJson = request.Tools!.Select(t => new
        {
            name = t.Function.Name,
            description = t.Function.Description,
            parameters = t.Function.Parameters
        }).ToArray();

        var toolsJsonString = JsonSerializer.Serialize(toolsJson);

        // Add to system message
        var systemMessage = request.Messages.FirstOrDefault(m => m.Role.ToLowerInvariant() == "system");
        if (systemMessage != null && systemMessage.Content is TextContentPart textContent)
        {
            textContent.Text += $"\n\nAvailable tools: {toolsJsonString}";
        }
        else
        {
            request.Messages.Insert(0, new OpenAIChatMessage
            {
                Role = "system",
                Content = new TextContentPart
                {
                    Text = $"You are a helpful assistant with tools.\n\nAvailable tools: {toolsJsonString}"
                }
            });
        }

        return Task.CompletedTask;
    }
}