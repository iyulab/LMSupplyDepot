using LLama;
using LLama.Native;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace LMSupplyDepots.External.LLamaEngine.Services;

/// <summary>
/// Service for dynamically applying chat templates based on model metadata
/// </summary>
public class DynamicChatTemplateService
{
    private readonly ILogger<DynamicChatTemplateService> _logger;
    private readonly ModelMetadataExtractor _metadataExtractor;

    public DynamicChatTemplateService(
        ILogger<DynamicChatTemplateService> logger,
        ModelMetadataExtractor metadataExtractor)
    {
        _logger = logger;
        _metadataExtractor = metadataExtractor;
    }

    /// <summary>
    /// Apply the model's native chat template to format messages
    /// </summary>
    public string ApplyChatTemplate(
        SafeLlamaModelHandle modelHandle,
        IEnumerable<ChatMessage> messages,
        bool addGenerationPrompt = true,
        ToolCallOptions? toolOptions = null)
    {
        try
        {
            var metadata = _metadataExtractor.ExtractMetadata(modelHandle);

            if (metadata.ChatTemplate != null)
            {
                return ApplyNativeChatTemplate(modelHandle, metadata, messages, addGenerationPrompt, toolOptions);
            }
            else
            {
                _logger.LogWarning("No native chat template found, falling back to architecture-specific formatting");
                return ApplyArchitectureSpecificTemplate(metadata, messages, addGenerationPrompt, toolOptions);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply chat template");
            throw;
        }
    }

    /// <summary>
    /// Apply the native chat template using metadata-driven approach
    /// </summary>
    private string ApplyNativeChatTemplate(
        SafeLlamaModelHandle modelHandle,
        ModelMetadata metadata,
        IEnumerable<ChatMessage> messages,
        bool addGenerationPrompt,
        ToolCallOptions? toolOptions)
    {
        try
        {
            // For now, use the chat template string directly for parsing and application
            // This is a simplified approach that can be enhanced with native API calls later
            var template = metadata.ChatTemplate!;

            // Apply basic template substitution
            var result = ApplyTemplateManually(template, messages, addGenerationPrompt, toolOptions, metadata);

            _logger.LogDebug("Successfully applied chat template using metadata");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply chat template, falling back to architecture-specific formatting");
            return ApplyArchitectureSpecificTemplate(metadata, messages, addGenerationPrompt, toolOptions);
        }
    }

    /// <summary>
    /// Manually apply chat template by parsing the template string
    /// </summary>
    private string ApplyTemplateManually(
        string template,
        IEnumerable<ChatMessage> messages,
        bool addGenerationPrompt,
        ToolCallOptions? toolOptions,
        ModelMetadata metadata)
    {
        // This is a simplified template engine
        // Real implementation would need a proper Jinja2-like parser

        var result = template;
        var messagesList = messages.ToList();

        // Simple substitutions for common template patterns
        if (template.Contains("{% for message in messages %}"))
        {
            // Extract the message loop content
            var loopStart = template.IndexOf("{% for message in messages %}");
            var loopEnd = template.IndexOf("{% endfor %}");

            if (loopStart >= 0 && loopEnd >= 0)
            {
                var beforeLoop = template.Substring(0, loopStart);
                var loopContent = template.Substring(loopStart + "{% for message in messages %}".Length,
                    loopEnd - loopStart - "{% for message in messages %}".Length);
                var afterLoop = template.Substring(loopEnd + "{% endfor %}".Length);

                var messagesText = string.Join("", messagesList.Select(msg =>
                    loopContent.Replace("{{ message.role }}", msg.Role)
                              .Replace("{{ message.content }}", msg.Content)));

                result = beforeLoop + messagesText + afterLoop;
            }
        }

        // Handle add_generation_prompt
        if (addGenerationPrompt && template.Contains("{% if add_generation_prompt %}"))
        {
            var promptStart = template.IndexOf("{% if add_generation_prompt %}");
            var promptEnd = template.IndexOf("{% endif %}");

            if (promptStart >= 0 && promptEnd >= 0)
            {
                var promptContent = template.Substring(promptStart + "{% if add_generation_prompt %}".Length,
                    promptEnd - promptStart - "{% if add_generation_prompt %}".Length);

                result = result.Replace("{% if add_generation_prompt %}" + promptContent + "{% endif %}", promptContent);
            }
        }
        else
        {
            // Remove the conditional block if not adding generation prompt
            result = System.Text.RegularExpressions.Regex.Replace(result,
                @"\{\% if add_generation_prompt \%\}.*?\{\% endif \%\}", "",
                System.Text.RegularExpressions.RegexOptions.Singleline);
        }

        // Handle special tokens
        if (metadata.SpecialTokens.TryGetValue("BOS", out var bosToken))
        {
            result = result.Replace("<|begin_of_text|>", "<BOS>"); // Placeholder for actual token
        }

        if (metadata.SpecialTokens.TryGetValue("EOS", out var eosToken))
        {
            result = result.Replace("<|eot_id|>", "<EOS>"); // Placeholder for actual token
        }

        return result;
    }

    /// <summary>
    /// Apply architecture-specific template when native template is not available
    /// </summary>
    private string ApplyArchitectureSpecificTemplate(
        ModelMetadata metadata,
        IEnumerable<ChatMessage> messages,
        bool addGenerationPrompt,
        ToolCallOptions? toolOptions)
    {
        return metadata.Architecture.ToLowerInvariant() switch
        {
            "phi3" => ApplyPhi3Template(metadata, messages, addGenerationPrompt, toolOptions),
            "llama" => ApplyLlamaTemplate(metadata, messages, addGenerationPrompt, toolOptions),
            "mixtral" => ApplyMixtralTemplate(metadata, messages, addGenerationPrompt, toolOptions),
            _ => ApplyGenericTemplate(metadata, messages, addGenerationPrompt, toolOptions)
        };
    }

    /// <summary>
    /// Apply Phi-3/Phi-4 specific chat template
    /// </summary>
    private string ApplyPhi3Template(
        ModelMetadata metadata,
        IEnumerable<ChatMessage> messages,
        bool addGenerationPrompt,
        ToolCallOptions? toolOptions)
    {
        var sb = new StringBuilder();

        foreach (var message in messages)
        {
            sb.Append($"<|{message.Role}|>\n");

            if (message.Role == "user" && toolOptions?.HasTools == true)
            {
                // Add tool information for Phi models
                sb.AppendLine(FormatToolsForPhi(toolOptions.Tools));
            }

            sb.AppendLine(message.Content);
            sb.AppendLine("<|end|>");
        }

        if (addGenerationPrompt)
        {
            sb.Append("<|assistant|>\n");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Apply Llama-specific chat template
    /// </summary>
    private string ApplyLlamaTemplate(
        ModelMetadata metadata,
        IEnumerable<ChatMessage> messages,
        bool addGenerationPrompt,
        ToolCallOptions? toolOptions)
    {
        var sb = new StringBuilder();

        // Add BOS token if available
        if (metadata.SpecialTokens.TryGetValue("BOS", out var bosToken))
        {
            sb.Append(GetTokenText(bosToken));
        }

        foreach (var message in messages)
        {
            sb.Append($"[INST] ");

            if (message.Role == "system")
            {
                sb.Append($"<<SYS>>\n{message.Content}\n<</SYS>>\n\n");
            }
            else if (message.Role == "user")
            {
                if (toolOptions?.HasTools == true)
                {
                    sb.AppendLine(FormatToolsForLlama(toolOptions.Tools));
                }
                sb.Append(message.Content);
            }

            sb.Append(" [/INST]");

            if (message.Role == "assistant")
            {
                sb.Append($" {message.Content}");

                // Add EOS token after assistant response
                if (metadata.SpecialTokens.TryGetValue("EOS", out var eosToken))
                {
                    sb.Append(GetTokenText(eosToken));
                }
            }
        }

        if (addGenerationPrompt && !messages.Last().Role.Equals("assistant"))
        {
            sb.Append(" ");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Apply Mixtral-specific chat template
    /// </summary>
    private string ApplyMixtralTemplate(
        ModelMetadata metadata,
        IEnumerable<ChatMessage> messages,
        bool addGenerationPrompt,
        ToolCallOptions? toolOptions)
    {
        // Mixtral uses a similar format to Llama but with different tokens
        return ApplyLlamaTemplate(metadata, messages, addGenerationPrompt, toolOptions);
    }

    /// <summary>
    /// Apply generic fallback template
    /// </summary>
    private string ApplyGenericTemplate(
        ModelMetadata metadata,
        IEnumerable<ChatMessage> messages,
        bool addGenerationPrompt,
        ToolCallOptions? toolOptions)
    {
        var sb = new StringBuilder();

        foreach (var message in messages)
        {
            sb.AppendLine($"{message.Role.ToUpperInvariant()}: {message.Content}");
        }

        if (addGenerationPrompt)
        {
            sb.Append("ASSISTANT: ");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Format tools for Phi models
    /// </summary>
    private string FormatToolsForPhi(IEnumerable<ToolDefinition> tools)
    {
        var toolsJson = JsonSerializer.Serialize(tools.Select(t => new
        {
            type = "function",
            function = new
            {
                name = t.Name,
                description = t.Description,
                parameters = t.Parameters
            }
        }), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        return $"You have access to the following tools:\n{toolsJson}\n\nUse tools by responding with:\n<|tool|>\n{{\"name\": \"tool_name\", \"arguments\": {{...}}}}\n<|end|>";
    }

    /// <summary>
    /// Format tools for Llama models
    /// </summary>
    private string FormatToolsForLlama(IEnumerable<ToolDefinition> tools)
    {
        var toolsJson = JsonSerializer.Serialize(tools.Select(t => new
        {
            type = "function",
            function = new
            {
                name = t.Name,
                description = t.Description,
                parameters = t.Parameters
            }
        }), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        return $"Available tools: {toolsJson}\n\n";
    }

    /// <summary>
    /// Convert messages to native chat message format
    /// </summary>
    private ChatMessage[] ConvertToNativeChatMessages(IEnumerable<ChatMessage> messages, ToolCallOptions? toolOptions)
    {
        var result = new List<ChatMessage>();

        foreach (var message in messages)
        {
            result.Add(message);

            // Add tool information as system message if needed
            if (message.Role == "user" && toolOptions?.HasTools == true && !result.Any(m => m.Role == "system"))
            {
                var toolsJson = JsonSerializer.Serialize(toolOptions.Tools,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

                result.Insert(0, new ChatMessage
                {
                    Role = "system",
                    Content = $"You have access to the following tools: {toolsJson}"
                });
            }
        }

        return result.ToArray();
    }

    /// <summary>
    /// Get text representation of a token
    /// </summary>
    private string GetTokenText(LLamaToken token)
    {
        // This would need to be implemented based on LlamaSharp's token-to-text conversion
        // For now, return empty string as placeholder
        return string.Empty;
    }
}

/// <summary>
/// Chat message structure
/// </summary>
public class ChatMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

/// <summary>
/// Tool calling options
/// </summary>
public class ToolCallOptions
{
    public bool HasTools => Tools.Any();
    public IEnumerable<ToolDefinition> Tools { get; set; } = Enumerable.Empty<ToolDefinition>();
}

/// <summary>
/// Tool definition structure
/// </summary>
public class ToolDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public object Parameters { get; set; } = new();
}
