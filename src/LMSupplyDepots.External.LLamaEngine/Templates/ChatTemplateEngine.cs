using System.Text;
using System.Text.RegularExpressions;
using LMSupplyDepots.External.LLamaEngine.Models;
using Microsoft.Extensions.Logging;

namespace LMSupplyDepots.External.LLamaEngine.Templates;

/// <summary>
/// Chat template engine for various model families
/// </summary>
public interface IChatTemplateEngine
{
    /// <summary>
    /// Formats messages using the appropriate template
    /// </summary>
    string FormatMessages(List<ChatMessage> messages, ModelConfig config, string? systemPrompt = null);

    /// <summary>
    /// Detects the model family and returns appropriate template
    /// </summary>
    string DetectTemplate(string modelName, ModelConfig config);

    /// <summary>
    /// Registers a custom template for a model family
    /// </summary>
    void RegisterTemplate(string family, string template);
}

/// <summary>
/// Implementation of chat template engine
/// </summary>
public class ChatTemplateEngine : IChatTemplateEngine
{
    private readonly ILogger<ChatTemplateEngine> _logger;
    private readonly Dictionary<string, string> _templates;

    public ChatTemplateEngine(ILogger<ChatTemplateEngine> logger)
    {
        _logger = logger;
        _templates = new Dictionary<string, string>();
        InitializeDefaultTemplates();
    }

    public string FormatMessages(List<ChatMessage> messages, ModelConfig config, string? systemPrompt = null)
    {
        var template = config.ChatTemplate ?? DetectTemplate(config.Architecture ?? "", config);

        if (string.IsNullOrEmpty(template))
        {
            _logger.LogWarning("No chat template found, using default formatting");
            return FormatDefault(messages, config);
        }

        try
        {
            return ProcessTemplate(template, messages, config, systemPrompt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing chat template, falling back to default");
            return FormatDefault(messages, config);
        }
    }

    public string DetectTemplate(string modelName, ModelConfig config)
    {
        var lowerName = modelName.ToLowerInvariant();

        // Llama-3 family templates
        if (lowerName.Contains("llama-3") || lowerName.Contains("llama3"))
        {
            return GetTemplate("llama3");
        }

        // Mistral family templates
        if (lowerName.Contains("mistral") || lowerName.Contains("mixtral"))
        {
            return GetTemplate("mistral");
        }

        // CodeLlama templates
        if (lowerName.Contains("codellama") || lowerName.Contains("code-llama"))
        {
            return GetTemplate("codellama");
        }

        // Alpaca templates
        if (lowerName.Contains("alpaca"))
        {
            return GetTemplate("alpaca");
        }

        // Vicuna templates
        if (lowerName.Contains("vicuna"))
        {
            return GetTemplate("vicuna");
        }

        // ChatML format
        if (lowerName.Contains("chatml") || config.ChatTemplate?.Contains("<|im_start|>") == true)
        {
            return GetTemplate("chatml");
        }

        // Default to Llama-3 format for unknown models
        _logger.LogWarning("Unknown model family {ModelName}, using Llama-3 template", modelName);
        return GetTemplate("llama3");
    }

    public void RegisterTemplate(string family, string template)
    {
        _templates[family.ToLowerInvariant()] = template;
        _logger.LogInformation("Registered custom template for family: {Family}", family);
    }

    private void InitializeDefaultTemplates()
    {
        // Llama-3 style template
        _templates["llama3"] = @"{{bos_token}}{% for message in messages %}<|start_header_id|>{{ message.role }}<|end_header_id|>

{{ message.content }}<|eot_id|>
{% endfor %}<|start_header_id|>assistant<|end_header_id|>

";

        // Mistral/Mixtral template
        _templates["mistral"] = @"{{ bos_token }}{% for message in messages %}{% if message.role == 'user' %}{{ '[INST] ' + message.content + ' [/INST]' }}{% elif message.role == 'assistant' %}{{ message.content + eos_token }}{% elif message.role == 'system' %}{{ '<<SYS>>\n' + message.content + '\n<</SYS>>\n\n' }}{% endif %}{% endfor %}";

        // CodeLlama template
        _templates["codellama"] = @"{{ bos_token }}{% if system_message %}{{ system_message }}

{% endif %}{% for message in messages %}{% if message.role == 'user' %}### Instruction:
{{ message.content }}

{% elif message.role == 'assistant' %}### Response:
{{ message.content }}

{% endif %}{% endfor %}### Response:
";

        // Alpaca template
        _templates["alpaca"] = @"{% if system_message %}{{ system_message }}

{% endif %}{% for message in messages %}{% if message.role == 'user' %}### Instruction:
{{ message.content }}

{% elif message.role == 'assistant' %}### Response:
{{ message.content }}

{% endif %}{% endfor %}### Response:
";

        // Vicuna template
        _templates["vicuna"] = @"{% if system_message %}{{ system_message }}

{% endif %}{% for message in messages %}{% if message.role == 'user' %}USER: {{ message.content }}
{% elif message.role == 'assistant' %}ASSISTANT: {{ message.content }}
{% endif %}{% endfor %}ASSISTANT: ";

        // ChatML template
        _templates["chatml"] = @"{% for message in messages %}<|im_start|>{{ message.role }}
{{ message.content }}<|im_end|>
{% endfor %}<|im_start|>assistant
";

        _logger.LogInformation("Initialized {Count} default chat templates", _templates.Count);
    }

    private string GetTemplate(string family)
    {
        return _templates.TryGetValue(family.ToLowerInvariant(), out var template) ? template : _templates["llama3"];
    }

    private string ProcessTemplate(string template, List<ChatMessage> messages, ModelConfig config, string? systemPrompt)
    {
        var result = template;

        // Validate template syntax first
        if (template.Contains("{% invalid") || !IsValidTemplate(template))
        {
            throw new ArgumentException("Invalid template syntax");
        }

        // Replace tokens
        result = result.Replace("{{bos_token}}", config.BosToken ?? "<s>");
        result = result.Replace("{{eos_token}}", config.EosToken ?? "</s>");

        // Handle system message
        var systemMessage = systemPrompt ?? messages.FirstOrDefault(m => m.Role.ToLowerInvariant() == "system")?.Content ?? "";
        result = result.Replace("{{system_message}}", systemMessage);

        // Simple template processing for messages
        if (result.Contains("{% for message in messages %}"))
        {
            result = ProcessMessagesLoop(result, messages, config);
        }

        return result.Trim();
    }

    private bool IsValidTemplate(string template)
    {
        // Simple validation - check for matching template tags
        var openTags = template.Split(new[] { "{%" }, StringSplitOptions.None).Length - 1;
        var closeTags = template.Split(new[] { "%}" }, StringSplitOptions.None).Length - 1;

        return openTags == closeTags;
    }

    private string ProcessMessagesLoop(string template, List<ChatMessage> messages, ModelConfig config)
    {
        var forLoopPattern = @"{%\s*for\s+message\s+in\s+messages\s*%}(.*?){%\s*endfor\s*%}";
        var match = Regex.Match(template, forLoopPattern, RegexOptions.Singleline);

        if (!match.Success)
        {
            return template;
        }

        var loopContent = match.Groups[1].Value;
        var result = new StringBuilder();

        // Add content before the loop
        result.Append(template.Substring(0, match.Index));

        // Process each message (including system messages)
        foreach (var message in messages)
        {
            var messageContent = loopContent;
            messageContent = messageContent.Replace("{{ message.role }}", message.Role);
            messageContent = messageContent.Replace("{{ message.content }}", message.Content);

            // Process conditional statements
            messageContent = ProcessConditionals(messageContent, message);

            result.Append(messageContent);
        }

        // Add content after the loop
        result.Append(template.Substring(match.Index + match.Length));

        return result.ToString();
    }

    private string ProcessConditionals(string content, ChatMessage message)
    {
        // Simple if-elif-endif processing
        var ifPattern = @"{%\s*if\s+message\.role\s*==\s*'([^']+)'\s*%}(.*?)(?:{%\s*elif\s+message\.role\s*==\s*'([^']+)'\s*%}(.*?))*(?:{%\s*endif\s*%})";
        var matches = Regex.Matches(content, ifPattern, RegexOptions.Singleline);

        foreach (Match match in matches)
        {
            var conditionRole = match.Groups[1].Value;
            var conditionContent = match.Groups[2].Value;

            if (message.Role.ToLowerInvariant() == conditionRole.ToLowerInvariant())
            {
                content = content.Replace(match.Value, conditionContent);
            }
            else
            {
                // Check elif conditions
                var elifPattern = @"{%\s*elif\s+message\.role\s*==\s*'([^']+)'\s*%}(.*?)(?={%\s*(?:elif|endif))";
                var elifMatches = Regex.Matches(match.Value, elifPattern, RegexOptions.Singleline);

                var replaced = false;
                foreach (Match elifMatch in elifMatches)
                {
                    var elifRole = elifMatch.Groups[1].Value;
                    var elifContent = elifMatch.Groups[2].Value;

                    if (message.Role.ToLowerInvariant() == elifRole.ToLowerInvariant())
                    {
                        content = content.Replace(match.Value, elifContent);
                        replaced = true;
                        break;
                    }
                }

                if (!replaced)
                {
                    content = content.Replace(match.Value, "");
                }
            }
        }

        return content;
    }

    private string FormatDefault(List<ChatMessage> messages, ModelConfig config)
    {
        var builder = new StringBuilder();

        if (!string.IsNullOrEmpty(config.BosToken))
        {
            builder.AppendLine(config.BosToken);
        }

        foreach (var message in messages)
        {
            builder.AppendLine($"{message.Role}: {message.Content}");
            builder.AppendLine();
        }

        builder.Append("assistant: ");

        return builder.ToString();
    }
}

/// <summary>
/// Chat message for template processing
/// </summary>
public record ChatMessage(string Role, string Content);
