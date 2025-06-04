using LMSupplyDepots.External.LLamaEngine.Models;

namespace LMSupplyDepots.External.LLamaEngine.Chat;

public record ChatMessage(string Role, string Content);

public class ChatHistory
{
    private readonly List<ChatMessage> _messages = [];
    private readonly string _systemPrompt;
    private readonly int _maxHistoryLength;
    private readonly ModelConfig? _modelConfig;

    public ChatHistory(string systemPrompt, ModelConfig? modelConfig = null, int maxHistoryLength = 10)
    {
        _systemPrompt = systemPrompt;
        _maxHistoryLength = maxHistoryLength;
        _modelConfig = modelConfig;
        _messages.Add(new ChatMessage("system", systemPrompt));
    }

    public void AddMessage(string role, string content)
    {
        _messages.Add(new ChatMessage(role, content));

        // Keep only the system prompt and last N messages if we exceed the limit
        if (_messages.Count > _maxHistoryLength + 1)  // +1 for system prompt
        {
            var systemPrompt = _messages[0];
            _messages.RemoveRange(1, _messages.Count - _maxHistoryLength - 1);
            _messages.Insert(0, systemPrompt);
        }
    }

    public string GetFormattedPrompt()
    {
        if (_modelConfig?.ChatTemplate != null)
        {
            return FormatWithTemplate();
        }

        return FormatDefault();
    }

    private string FormatWithTemplate()
    {
        // Simple template processing implementation
        // In a real implementation, a more complex template engine could be used
        var builder = new System.Text.StringBuilder();

        if (!string.IsNullOrEmpty(_modelConfig?.BosToken))
        {
            builder.AppendLine(_modelConfig.BosToken);
        }

        foreach (var message in _messages)
        {
            builder.AppendLine($"<|start_header_id|>{message.Role}<|end_header_id|>\n");
            builder.AppendLine(message.Content);
            builder.AppendLine("<|eot_id|>");
        }

        return builder.ToString();
    }

    private string FormatDefault()
    {
        var builder = new System.Text.StringBuilder();

        foreach (var message in _messages)
        {
            builder.AppendLine($"{message.Role}: {message.Content}");
            builder.AppendLine();
        }

        return builder.ToString();
    }

    public void Clear()
    {
        _messages.Clear();
        _messages.Add(new ChatMessage("system", _systemPrompt));
    }

    public IReadOnlyList<ChatMessage> Messages => _messages.AsReadOnly();
}