using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace LMSupplyDepots.Host.Services;

/// <summary>
/// Service for processing reasoning/thinking content in AI responses
/// </summary>
public interface IReasoningService
{
    /// <summary>
    /// Process reasoning content from model response
    /// </summary>
    Task<ReasoningResult> ProcessReasoningAsync(string content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if content contains reasoning/thinking patterns
    /// </summary>
    bool IsReasoningResponse(string content);

    /// <summary>
    /// Extract thinking content from the response
    /// </summary>
    string ExtractThinking(string content);

    /// <summary>
    /// Extract final answer after thinking
    /// </summary>
    string ExtractFinalAnswer(string content);

    /// <summary>
    /// Count tokens in thinking content
    /// </summary>
    int CountReasoningTokens(string thinkingContent);
}

/// <summary>
/// Result of reasoning content processing
/// </summary>
public class ReasoningResult
{
    /// <summary>
    /// The thinking/reasoning content
    /// </summary>
    public string ThinkingContent { get; set; } = string.Empty;

    /// <summary>
    /// The final answer after reasoning
    /// </summary>
    public string FinalAnswer { get; set; } = string.Empty;

    /// <summary>
    /// Number of tokens used for reasoning
    /// </summary>
    public int ReasoningTokens { get; set; }

    /// <summary>
    /// Whether reasoning content was found
    /// </summary>
    public bool HasReasoning { get; set; }

    /// <summary>
    /// The original content before processing
    /// </summary>
    public string OriginalContent { get; set; } = string.Empty;
}

/// <summary>
/// Implementation of reasoning service
/// </summary>
public class ReasoningService : IReasoningService
{
    private readonly ILogger<ReasoningService> _logger;

    // Patterns for detecting thinking content (with capture groups for extraction)
    private static readonly Regex[] ThinkingPatterns = new[]
    {
        new Regex(@"<thinking>(.*?)</thinking>", RegexOptions.Singleline | RegexOptions.IgnoreCase),
        new Regex(@"<reasoning>(.*?)</reasoning>", RegexOptions.Singleline | RegexOptions.IgnoreCase),
        new Regex(@"<internal>(.*?)</internal>", RegexOptions.Singleline | RegexOptions.IgnoreCase),
        new Regex(@"<thought>(.*?)</thought>", RegexOptions.Singleline | RegexOptions.IgnoreCase),
        new Regex(@"\*\*Thinking:\*\*(.*?)(?=\n\*\*Answer:\*\*|\n\*\*Response:\*\*|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase),
        new Regex(@"Let me think about this\.\.\.(.*?)(?=\n\n|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase)
    };

    // Patterns for removing thinking content (without capture groups)
    private static readonly Regex[] ThinkingRemovalPatterns = new[]
    {
        new Regex(@"<thinking>[\s\S]*?</thinking>\s*", RegexOptions.IgnoreCase),
        new Regex(@"<reasoning>[\s\S]*?</reasoning>\s*", RegexOptions.IgnoreCase),
        new Regex(@"<internal>[\s\S]*?</internal>\s*", RegexOptions.IgnoreCase),
        new Regex(@"<thought>[\s\S]*?</thought>\s*", RegexOptions.IgnoreCase),
        new Regex(@"\*\*Thinking:\*\*[\s\S]*?(?=\n\*\*Answer:\*\*|\n\*\*Response:\*\*|$)", RegexOptions.IgnoreCase),
        new Regex(@"Let me think about this\.\.\.[\s\S]*?(?=\n\n|$)", RegexOptions.IgnoreCase)
    };

    // Patterns for detecting final answer
    private static readonly Regex[] AnswerPatterns = new[]
    {
        new Regex(@"<answer>(.*?)</answer>", RegexOptions.Singleline | RegexOptions.IgnoreCase),
        new Regex(@"\*\*Answer:\*\*(.*?)(?=\n|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase),
        new Regex(@"\*\*Response:\*\*(.*?)(?=\n|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase),
        new Regex(@"\nTherefore,?\s*(.*?)(?=\n|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase),
        new Regex(@"(\n|^)So,?\s*(.*?)(?=\n|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase),
        new Regex(@"(\n|^)Thus,?\s*(.*?)(?=\n|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase),
        new Regex(@"(\n|^)In conclusion,?\s*(.*?)(?=\n|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase)
    };

    public ReasoningService(ILogger<ReasoningService> logger)
    {
        _logger = logger;
    }

    public async Task<ReasoningResult> ProcessReasoningAsync(string content, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Processing reasoning content, length: {Length}", content?.Length ?? 0);

            if (string.IsNullOrEmpty(content))
            {
                return new ReasoningResult { OriginalContent = content ?? string.Empty };
            }

            var result = new ReasoningResult
            {
                OriginalContent = content,
                HasReasoning = IsReasoningResponse(content)
            };

            if (result.HasReasoning)
            {
                result.ThinkingContent = ExtractThinking(content);
                result.FinalAnswer = ExtractFinalAnswer(content);
                result.ReasoningTokens = CountReasoningTokens(result.ThinkingContent);

                _logger.LogDebug("Extracted reasoning: thinking={ThinkingLength}, answer={AnswerLength}, tokens={ReasoningTokens}",
                    result.ThinkingContent.Length, result.FinalAnswer.Length, result.ReasoningTokens);
            }
            else
            {
                // If no reasoning pattern found, treat entire content as final answer
                result.FinalAnswer = content;
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing reasoning content");
            return new ReasoningResult
            {
                OriginalContent = content ?? string.Empty,
                FinalAnswer = content ?? string.Empty
            };
        }
    }

    public bool IsReasoningResponse(string content)
    {
        if (string.IsNullOrEmpty(content))
            return false;

        return ThinkingPatterns.Any(pattern => pattern.IsMatch(content));
    }

    public string ExtractThinking(string content)
    {
        if (string.IsNullOrEmpty(content))
            return string.Empty;

        foreach (var pattern in ThinkingPatterns)
        {
            var match = pattern.Match(content);
            if (match.Success && match.Groups.Count > 1)
            {
                return match.Groups[1].Value;
            }
        }

        return string.Empty;
    }

    public string ExtractFinalAnswer(string content)
    {
        if (string.IsNullOrEmpty(content))
            return string.Empty;

        // First try to extract explicit answer patterns
        foreach (var pattern in AnswerPatterns)
        {
            var match = pattern.Match(content);
            if (match.Success && match.Groups.Count > 1)
            {
                // For patterns with (\n|^) prefix, use Groups[2], otherwise Groups[1]
                var result = match.Groups.Count > 2 && !string.IsNullOrEmpty(match.Groups[2].Value)
                    ? match.Groups[2].Value
                    : match.Groups[1].Value;

                // Don't trim for "Answer", "Response", "Therefore", "So", "Thus", "In conclusion" patterns to preserve leading space
                if (pattern.ToString().Contains("Answer") || pattern.ToString().Contains("Response") ||
                    pattern.ToString().Contains("Therefore") || pattern.ToString().Contains("So,") ||
                    pattern.ToString().Contains("Thus") || pattern.ToString().Contains("conclusion"))
                {
                    return result;
                }
                return result.Trim();
            }
        }

        // If no explicit answer pattern, remove thinking content and return the rest
        var contentWithoutThinking = content;

        // Apply removal patterns in order, ensuring complete removal
        foreach (var pattern in ThinkingRemovalPatterns)
        {
            contentWithoutThinking = pattern.Replace(contentWithoutThinking, string.Empty);
        }

        // Additional cleanup for any remaining artifacts
        contentWithoutThinking = System.Text.RegularExpressions.Regex.Replace(
            contentWithoutThinking,
            @"ning>\s*</thinking>",
            "",
            RegexOptions.IgnoreCase);

        // Clean up multiple whitespaces and newlines
        contentWithoutThinking = System.Text.RegularExpressions.Regex.Replace(
            contentWithoutThinking,
            @"\s+",
            " ",
            RegexOptions.Multiline);

        return contentWithoutThinking.Trim();
    }

    public int CountReasoningTokens(string thinkingContent)
    {
        if (string.IsNullOrEmpty(thinkingContent))
            return 0;

        // Simple approximation: ~4 characters per token for English text
        // This is a rough estimate - in production, you'd use the actual tokenizer
        var approximateTokens = Math.Max(1, thinkingContent.Length / 4);

        // Add some tokens for the thinking tags themselves
        return approximateTokens + 2;
    }
}