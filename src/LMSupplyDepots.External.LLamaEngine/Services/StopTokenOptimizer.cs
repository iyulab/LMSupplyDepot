using Microsoft.Extensions.Logging;
using LMSupplyDepots.External.LLamaEngine.Models;

namespace LMSupplyDepots.External.LLamaEngine.Services;

/// <summary>
/// Advanced stop token optimization service for model-specific termination conditions
/// </summary>
public interface IStopTokenOptimizer
{
    /// <summary>
    /// Optimizes stop tokens for specific model architecture and context
    /// </summary>
    OptimizedStopTokens OptimizeStopTokens(string modelArchitecture, List<string> requestStopTokens, ModelOptimizationContext context);

    /// <summary>
    /// Validates stop tokens for compatibility with model architecture
    /// </summary>
    StopTokenValidationResult ValidateStopTokens(string modelArchitecture, List<string> stopTokens);

    /// <summary>
    /// Gets model-specific stop token recommendations
    /// </summary>
    List<string> GetRecommendedStopTokens(string modelArchitecture, string modelName, ModelOptimizationContext context);

    /// <summary>
    /// Detects potential stop token conflicts in generation output
    /// </summary>
    List<StopTokenConflict> DetectConflicts(string generatedText, List<string> appliedStopTokens);
}

/// <summary>
/// Model optimization context for stop token decisions
/// </summary>
public class ModelOptimizationContext
{
    /// <summary>
    /// Whether model supports function/tool calling
    /// </summary>
    public bool SupportsToolCalling { get; set; }

    /// <summary>
    /// Chat template format being used
    /// </summary>
    public string? ChatTemplateFormat { get; set; }

    /// <summary>
    /// Whether conversation includes system messages
    /// </summary>
    public bool HasSystemMessages { get; set; }

    /// <summary>
    /// Whether conversation includes tool call results
    /// </summary>
    public bool HasToolResults { get; set; }

    /// <summary>
    /// Expected generation length (short, medium, long)
    /// </summary>
    public GenerationLength ExpectedLength { get; set; } = GenerationLength.Medium;

    /// <summary>
    /// Whether to prioritize safety (conservative stopping) or completeness
    /// </summary>
    public StopTokenStrategy Strategy { get; set; } = StopTokenStrategy.Balanced;

    /// <summary>
    /// Maximum tokens for generation
    /// </summary>
    public int MaxTokens { get; set; } = 256;

    /// <summary>
    /// Temperature setting (affects stop token sensitivity)
    /// </summary>
    public float Temperature { get; set; } = 0.7f;
}

/// <summary>
/// Generation length categories
/// </summary>
public enum GenerationLength
{
    Short,      // < 50 tokens
    Medium,     // 50-200 tokens
    Long,       // 200-1000 tokens
    VeryLong    // > 1000 tokens
}

/// <summary>
/// Stop token optimization strategies
/// </summary>
public enum StopTokenStrategy
{
    Conservative,   // Prioritize early stopping to prevent issues
    Balanced,       // Balance between completion and safety
    Permissive      // Allow longer generation, minimal stopping
}

/// <summary>
/// Optimized stop token configuration
/// </summary>
public class OptimizedStopTokens
{
    /// <summary>
    /// Primary stop tokens (most important)
    /// </summary>
    public List<string> PrimaryStopTokens { get; set; } = new();

    /// <summary>
    /// Secondary stop tokens (safety fallbacks)
    /// </summary>
    public List<string> SecondaryStopTokens { get; set; } = new();

    /// <summary>
    /// Context-aware stop tokens (based on conversation state)
    /// </summary>
    public List<string> ContextStopTokens { get; set; } = new();

    /// <summary>
    /// Stop tokens that were filtered out due to conflicts
    /// </summary>
    public List<string> FilteredStopTokens { get; set; } = new();

    /// <summary>
    /// Explanation of optimization decisions
    /// </summary>
    public string OptimizationReasoning { get; set; } = string.Empty;

    /// <summary>
    /// Get all stop tokens in priority order
    /// </summary>
    public List<string> GetAllStopTokens()
    {
        var all = new List<string>();
        all.AddRange(PrimaryStopTokens);
        all.AddRange(SecondaryStopTokens);
        all.AddRange(ContextStopTokens);
        return all.Distinct().ToList();
    }
}

/// <summary>
/// Stop token validation result
/// </summary>
public class StopTokenValidationResult
{
    /// <summary>
    /// Whether all stop tokens are valid for the architecture
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Validation issues found
    /// </summary>
    public List<StopTokenIssue> Issues { get; set; } = new();

    /// <summary>
    /// Suggested corrections
    /// </summary>
    public List<string> Suggestions { get; set; } = new();
}

/// <summary>
/// Stop token validation issue
/// </summary>
public class StopTokenIssue
{
    /// <summary>
    /// Problematic stop token
    /// </summary>
    public string StopToken { get; set; } = string.Empty;

    /// <summary>
    /// Issue severity
    /// </summary>
    public IssueSeverity Severity { get; set; }

    /// <summary>
    /// Description of the issue
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Recommended action
    /// </summary>
    public string Recommendation { get; set; } = string.Empty;
}

/// <summary>
/// Issue severity levels
/// </summary>
public enum IssueSeverity
{
    Info,       // Informational, no action needed
    Warning,    // Potential issue, consider changes
    Error,      // Definite problem, must fix
    Critical    // Severe issue, will break generation
}

/// <summary>
/// Detected stop token conflict
/// </summary>
public class StopTokenConflict
{
    /// <summary>
    /// Stop token involved in conflict
    /// </summary>
    public string StopToken { get; set; } = string.Empty;

    /// <summary>
    /// Position in text where conflict occurred
    /// </summary>
    public int Position { get; set; }

    /// <summary>
    /// Context around the conflict
    /// </summary>
    public string Context { get; set; } = string.Empty;

    /// <summary>
    /// Type of conflict
    /// </summary>
    public ConflictType Type { get; set; }

    /// <summary>
    /// Impact of the conflict
    /// </summary>
    public string Impact { get; set; } = string.Empty;
}

/// <summary>
/// Types of stop token conflicts
/// </summary>
public enum ConflictType
{
    PrematureStop,      // Stopped too early due to token appearing in content
    TemplateInterference, // Stop token interfered with template structure
    PartialMatch,       // Partial match caused unexpected behavior
    EncodingIssue      // Token encoding caused problems
}

/// <summary>
/// Implementation of advanced stop token optimizer
/// </summary>
public class StopTokenOptimizer : IStopTokenOptimizer
{
    private readonly ILogger<StopTokenOptimizer> _logger;

    // Architecture-specific stop token configurations
    private static readonly Dictionary<string, ArchitectureStopConfig> ArchitectureConfigs = new()
    {
        ["llama"] = new()
        {
            PrimaryStops = new[] { "<|eot_id|>", "</s>" },
            SafetyStops = new[] { "<|start_header_id|>user<|end_header_id|>", "\nUser:", "\nHuman:" },
            ProblematicTokens = new[] { "\n", "\\n" }, // Can interfere with structure
            ToolStops = new[] { "<|python_tag|>", "<|eot_id|>" },
            MaxRecommendedStops = 5
        },
        ["phi3"] = new()
        {
            PrimaryStops = new[] { "<|end|>" },
            SafetyStops = new[] { "<|user|>", "<|system|>", "\nUser:", "\nHuman:" },
            ProblematicTokens = new[] { "\n", "\\n", "<|assistant|>" }, // Template interference
            ToolStops = new[] { "<|tool|>", "<|end|>" },
            MaxRecommendedStops = 3
        },
        ["mistral"] = new()
        {
            PrimaryStops = new[] { "</s>" },
            SafetyStops = new[] { "[INST]", "\nUser:", "\nHuman:" },
            ProblematicTokens = new[] { "\n", "\\n", "[/INST]" },
            ToolStops = new[] { "[TOOL_CALLS]", "[/TOOL_CALLS]" },
            MaxRecommendedStops = 4
        },
        ["qwen"] = new()
        {
            PrimaryStops = new[] { "<|im_end|>" },
            SafetyStops = new[] { "<|im_start|>user", "<|im_start|>system" },
            ProblematicTokens = new[] { "\n", "\\n", "<|im_start|>" },
            ToolStops = new[] { "<|im_start|>tool", "<|im_end|>" },
            MaxRecommendedStops = 4
        },
        ["gemma"] = new()
        {
            PrimaryStops = new[] { "<end_of_turn>" },
            SafetyStops = new[] { "<start_of_turn>user", "<start_of_turn>system" },
            ProblematicTokens = new[] { "\n", "\\n", "<start_of_turn>" },
            ToolStops = new[] { "<start_of_turn>tool", "<end_of_turn>" },
            MaxRecommendedStops = 4
        },
        ["deepseek"] = new()
        {
            PrimaryStops = new[] { "User:", "System:" },
            SafetyStops = new[] { "\nUser:", "\nSystem:", "\nHuman:" },
            ProblematicTokens = new[] { "\n", "\\n" },
            ToolStops = new[] { "```tool_call", "```" },
            MaxRecommendedStops = 5
        }
    };

    public StopTokenOptimizer(ILogger<StopTokenOptimizer> logger)
    {
        _logger = logger;
    }

    public OptimizedStopTokens OptimizeStopTokens(string modelArchitecture, List<string> requestStopTokens, ModelOptimizationContext context)
    {
        var arch = modelArchitecture.ToLowerInvariant();
        var config = GetArchitectureConfig(arch);
        var result = new OptimizedStopTokens();

        _logger.LogDebug("Optimizing stop tokens for architecture: {Architecture}, context: {Context}",
            arch, System.Text.Json.JsonSerializer.Serialize(context));

        // Step 1: Validate and filter request stop tokens
        var validatedTokens = ValidateAndFilterRequestTokens(requestStopTokens, config, context);

        // Step 2: Add primary architecture-specific stop tokens
        result.PrimaryStopTokens.AddRange(config.PrimaryStops);

        // Step 3: Add validated request tokens that don't conflict
        result.PrimaryStopTokens.AddRange(validatedTokens.ValidTokens);
        result.FilteredStopTokens.AddRange(validatedTokens.FilteredTokens);

        // Step 4: Add context-aware stop tokens
        AddContextAwareStopTokens(result, config, context);

        // Step 5: Add safety stop tokens based on strategy
        AddSafetyStopTokens(result, config, context);

        // Step 6: Limit total stop tokens to prevent performance issues
        LimitStopTokens(result, config, context);

        // Generate reasoning
        result.OptimizationReasoning = GenerateOptimizationReasoning(arch, context, result, validatedTokens.FilteredTokens);

        _logger.LogInformation("Optimized stop tokens for {Architecture}: {Count} total tokens, {Filtered} filtered",
            arch, result.GetAllStopTokens().Count, result.FilteredStopTokens.Count);

        return result;
    }

    public StopTokenValidationResult ValidateStopTokens(string modelArchitecture, List<string> stopTokens)
    {
        var arch = modelArchitecture.ToLowerInvariant();
        var config = GetArchitectureConfig(arch);
        var result = new StopTokenValidationResult { IsValid = true };

        foreach (var token in stopTokens)
        {
            var issues = ValidateStopToken(token, config, arch);
            result.Issues.AddRange(issues);

            if (issues.Any(i => i.Severity >= IssueSeverity.Error))
            {
                result.IsValid = false;
            }
        }

        // Generate suggestions
        result.Suggestions = GenerateValidationSuggestions(result.Issues, config);

        return result;
    }

    public List<string> GetRecommendedStopTokens(string modelArchitecture, string modelName, ModelOptimizationContext context)
    {
        var arch = modelArchitecture.ToLowerInvariant();
        var config = GetArchitectureConfig(arch);
        var recommended = new List<string>();

        // Always include primary architecture stops
        recommended.AddRange(config.PrimaryStops);

        // Add tool-specific stops if needed
        if (context.SupportsToolCalling)
        {
            recommended.AddRange(config.ToolStops);
        }

        // Add safety stops based on strategy
        if (context.Strategy == StopTokenStrategy.Conservative)
        {
            recommended.AddRange(config.SafetyStops);
        }
        else if (context.Strategy == StopTokenStrategy.Balanced)
        {
            // Add selective safety stops
            recommended.AddRange(config.SafetyStops.Take(2));
        }

        // Model-specific adjustments
        ApplyModelSpecificAdjustments(recommended, modelName, arch, context);

        return recommended.Distinct().Take(config.MaxRecommendedStops).ToList();
    }

    public List<StopTokenConflict> DetectConflicts(string generatedText, List<string> appliedStopTokens)
    {
        var conflicts = new List<StopTokenConflict>();

        foreach (var stopToken in appliedStopTokens)
        {
            var positions = FindTokenPositions(generatedText, stopToken);

            foreach (var position in positions)
            {
                var conflict = AnalyzeTokenPosition(generatedText, stopToken, position);
                if (conflict != null)
                {
                    conflicts.Add(conflict);
                }
            }
        }

        return conflicts;
    }

    #region Private Helper Methods

    private ArchitectureStopConfig GetArchitectureConfig(string architecture)
    {
        return ArchitectureConfigs.TryGetValue(architecture, out var config)
            ? config
            : ArchitectureConfigs["llama"]; // Default fallback
    }

    private (List<string> ValidTokens, List<string> FilteredTokens) ValidateAndFilterRequestTokens(
        List<string> requestTokens, ArchitectureStopConfig config, ModelOptimizationContext context)
    {
        var validTokens = new List<string>();
        var filteredTokens = new List<string>();

        foreach (var token in requestTokens)
        {
            if (IsTokenProblematic(token, config))
            {
                filteredTokens.Add(token);
                _logger.LogWarning("Filtered problematic stop token: {Token}", token);
            }
            else if (IsTokenRedundant(token, config.PrimaryStops))
            {
                _logger.LogDebug("Skipping redundant stop token: {Token}", token);
            }
            else
            {
                validTokens.Add(token);
            }
        }

        return (validTokens, filteredTokens);
    }

    private bool IsTokenProblematic(string token, ArchitectureStopConfig config)
    {
        return config.ProblematicTokens.Any(problematic =>
            token.Equals(problematic, StringComparison.OrdinalIgnoreCase) ||
            token.Contains(problematic) ||
            problematic.Contains(token));
    }

    private bool IsTokenRedundant(string token, string[] primaryStops)
    {
        return primaryStops.Any(primary =>
            primary.Equals(token, StringComparison.OrdinalIgnoreCase) ||
            primary.Contains(token));
    }

    private void AddContextAwareStopTokens(OptimizedStopTokens result, ArchitectureStopConfig config, ModelOptimizationContext context)
    {
        // Add tool-specific stops
        if (context.SupportsToolCalling)
        {
            result.ContextStopTokens.AddRange(config.ToolStops);
        }

        // Add length-specific stops
        if (context.ExpectedLength == GenerationLength.Short)
        {
            // For short generations, add more aggressive stopping
            result.ContextStopTokens.Add("\n\n");
        }
        else if (context.ExpectedLength == GenerationLength.VeryLong)
        {
            // For long generations, be more permissive
            result.ContextStopTokens.Clear();
        }

        // Temperature-based adjustments
        if (context.Temperature > 1.0f)
        {
            // High temperature needs more safety stops
            result.ContextStopTokens.AddRange(config.SafetyStops.Take(2));
        }
    }

    private void AddSafetyStopTokens(OptimizedStopTokens result, ArchitectureStopConfig config, ModelOptimizationContext context)
    {
        switch (context.Strategy)
        {
            case StopTokenStrategy.Conservative:
                result.SecondaryStopTokens.AddRange(config.SafetyStops);
                break;
            case StopTokenStrategy.Balanced:
                result.SecondaryStopTokens.AddRange(config.SafetyStops.Take(2));
                break;
            case StopTokenStrategy.Permissive:
                // Minimal safety stops
                result.SecondaryStopTokens.AddRange(config.SafetyStops.Take(1));
                break;
        }
    }

    private void LimitStopTokens(OptimizedStopTokens result, ArchitectureStopConfig config, ModelOptimizationContext context)
    {
        var allTokens = result.GetAllStopTokens();
        var maxTokens = context.Strategy switch
        {
            StopTokenStrategy.Conservative => Math.Min(config.MaxRecommendedStops + 2, 8),
            StopTokenStrategy.Balanced => config.MaxRecommendedStops,
            StopTokenStrategy.Permissive => Math.Max(config.MaxRecommendedStops - 1, 2),
            _ => config.MaxRecommendedStops
        };

        if (allTokens.Count > maxTokens)
        {
            // Prioritize: Primary > Context > Secondary
            var limited = new List<string>();
            limited.AddRange(result.PrimaryStopTokens.Take(maxTokens / 2));
            limited.AddRange(result.ContextStopTokens.Take((maxTokens - limited.Count) / 2));
            limited.AddRange(result.SecondaryStopTokens.Take(maxTokens - limited.Count));

            var removed = allTokens.Except(limited).ToList();
            result.FilteredStopTokens.AddRange(removed);

            // Redistribute
            result.PrimaryStopTokens = limited.Take(result.PrimaryStopTokens.Count).ToList();
            result.ContextStopTokens = limited.Skip(result.PrimaryStopTokens.Count).Take(result.ContextStopTokens.Count).ToList();
            result.SecondaryStopTokens = limited.Skip(result.PrimaryStopTokens.Count + result.ContextStopTokens.Count).ToList();

            _logger.LogInformation("Limited stop tokens from {Original} to {Limited}, removed: {Removed}",
                allTokens.Count, limited.Count, string.Join(", ", removed));
        }
    }

    private List<StopTokenIssue> ValidateStopToken(string token, ArchitectureStopConfig config, string architecture)
    {
        var issues = new List<StopTokenIssue>();

        // Check for problematic tokens
        if (config.ProblematicTokens.Contains(token))
        {
            issues.Add(new StopTokenIssue
            {
                StopToken = token,
                Severity = IssueSeverity.Error,
                Description = $"Token '{token}' conflicts with {architecture} template structure",
                Recommendation = "Remove this token or use architecture-specific alternatives"
            });
        }

        // Check for encoding issues
        if (HasEncodingIssues(token))
        {
            issues.Add(new StopTokenIssue
            {
                StopToken = token,
                Severity = IssueSeverity.Warning,
                Description = "Token may have encoding issues",
                Recommendation = "Verify token encoding compatibility"
            });
        }

        // Check for overly broad tokens
        if (IsOverlyBroad(token))
        {
            issues.Add(new StopTokenIssue
            {
                StopToken = token,
                Severity = IssueSeverity.Warning,
                Description = "Token is very broad and may cause premature stopping",
                Recommendation = "Consider more specific stop token"
            });
        }

        return issues;
    }

    private bool HasEncodingIssues(string token)
    {
        // Check for unusual characters or encoding artifacts
        return token.Any(c => char.IsControl(c) && c != '\n' && c != '\r' && c != '\t');
    }

    private bool IsOverlyBroad(string token)
    {
        // Very short tokens that could appear frequently
        return token.Length <= 2 && !new[] { "\n", "\r\n", "</s>", "<|", "|>" }.Contains(token);
    }

    private List<string> GenerateValidationSuggestions(List<StopTokenIssue> issues, ArchitectureStopConfig config)
    {
        var suggestions = new List<string>();

        if (issues.Any(i => i.Description.Contains("template structure")))
        {
            suggestions.Add($"Use primary architecture stops: {string.Join(", ", config.PrimaryStops)}");
        }

        if (issues.Any(i => i.Description.Contains("encoding")))
        {
            suggestions.Add("Verify all stop tokens use UTF-8 encoding");
        }

        if (issues.Any(i => i.Description.Contains("broad")))
        {
            suggestions.Add("Use longer, more specific stop tokens to prevent false positives");
        }

        return suggestions;
    }

    private void ApplyModelSpecificAdjustments(List<string> recommended, string modelName, string architecture, ModelOptimizationContext context)
    {
        var lowerName = modelName.ToLowerInvariant();

        // Model family specific adjustments
        if (lowerName.Contains("instruct") || lowerName.Contains("chat"))
        {
            // Instruction-tuned models need more conservative stopping
            if (!recommended.Contains("\nUser:") && context.Strategy != StopTokenStrategy.Permissive)
            {
                recommended.Add("\nUser:");
            }
        }

        if (lowerName.Contains("code") || lowerName.Contains("coder"))
        {
            // Code models might need specific stops
            if (!recommended.Contains("```") && context.SupportsToolCalling)
            {
                recommended.Add("```");
            }
        }

        // Size-based adjustments
        if (lowerName.Contains("7b") || lowerName.Contains("small"))
        {
            // Smaller models might need more guidance
            if (context.Strategy == StopTokenStrategy.Balanced)
            {
                context.Strategy = StopTokenStrategy.Conservative;
            }
        }
    }

    private string GenerateOptimizationReasoning(string architecture, ModelOptimizationContext context, OptimizedStopTokens result, List<string> filteredTokens)
    {
        var reasoning = new List<string>
        {
            $"Architecture: {architecture} - using {result.PrimaryStopTokens.Count} primary stops",
            $"Strategy: {context.Strategy} - {result.SecondaryStopTokens.Count} safety stops added",
            $"Context: Tool calling={context.SupportsToolCalling}, Length={context.ExpectedLength}"
        };

        if (filteredTokens.Any())
        {
            reasoning.Add($"Filtered {filteredTokens.Count} problematic tokens: {string.Join(", ", filteredTokens.Take(3))}");
        }

        if (result.ContextStopTokens.Any())
        {
            reasoning.Add($"Added {result.ContextStopTokens.Count} context-specific stops");
        }

        return string.Join("; ", reasoning);
    }

    private List<int> FindTokenPositions(string text, string token)
    {
        var positions = new List<int>();
        var index = 0;

        while ((index = text.IndexOf(token, index, StringComparison.OrdinalIgnoreCase)) != -1)
        {
            positions.Add(index);
            index += token.Length;
        }

        return positions;
    }

    private StopTokenConflict? AnalyzeTokenPosition(string text, string token, int position)
    {
        // Get context around the position
        var contextStart = Math.Max(0, position - 20);
        var contextEnd = Math.Min(text.Length, position + token.Length + 20);
        var context = text.Substring(contextStart, contextEnd - contextStart);

        // Analyze if this looks like a legitimate stop vs. content
        if (IsLikelyContentToken(text, token, position))
        {
            return new StopTokenConflict
            {
                StopToken = token,
                Position = position,
                Context = context,
                Type = ConflictType.PrematureStop,
                Impact = "May have stopped generation prematurely in the middle of content"
            };
        }

        // Check for template interference
        if (IsTemplateInterference(text, token, position))
        {
            return new StopTokenConflict
            {
                StopToken = token,
                Position = position,
                Context = context,
                Type = ConflictType.TemplateInterference,
                Impact = "Stop token interfered with chat template structure"
            };
        }

        return null; // No conflict detected
    }

    private bool IsLikelyContentToken(string text, string token, int position)
    {
        // Check if token appears within quoted content
        var beforePosition = Math.Max(0, position - 50);
        var afterPosition = Math.Min(text.Length, position + token.Length + 50);
        var context = text.Substring(beforePosition, afterPosition - beforePosition);

        // Look for quotes around the token
        var tokenInContext = position - beforePosition;
        var beforeToken = context.Substring(0, tokenInContext);
        var afterToken = context.Substring(tokenInContext + token.Length);

        // Count quotes before and after to see if we're inside quotes
        var quotesBefore = beforeToken.Count(c => c == '"' || c == '\'');
        var quotesAfter = afterToken.Count(c => c == '"' || c == '\'');

        // If odd number of quotes before (inside quotes) or appears mid-sentence
        if (quotesBefore % 2 == 1)
            return true;

        // Simple heuristic: if the token appears in the middle of a word or sentence
        var beforeChar = position > 0 ? text[position - 1] : ' ';
        var afterChar = position + token.Length < text.Length ? text[position + token.Length] : ' ';

        // If surrounded by alphanumeric characters, likely content
        return char.IsLetterOrDigit(beforeChar) && char.IsLetterOrDigit(afterChar);
    }

    private bool IsTemplateInterference(string text, string token, int position)
    {
        // Check if the token appears to be breaking template structure
        var surrounding = text.Substring(Math.Max(0, position - 10), Math.Min(20, text.Length - Math.Max(0, position - 10)));

        // Look for template-like patterns being interrupted
        return surrounding.Contains("<|") || surrounding.Contains("|>") ||
               surrounding.Contains("<im_") || surrounding.Contains("header_id");
    }

    #endregion
}

/// <summary>
/// Architecture-specific stop token configuration
/// </summary>
internal class ArchitectureStopConfig
{
    public string[] PrimaryStops { get; set; } = Array.Empty<string>();
    public string[] SafetyStops { get; set; } = Array.Empty<string>();
    public string[] ProblematicTokens { get; set; } = Array.Empty<string>();
    public string[] ToolStops { get; set; } = Array.Empty<string>();
    public int MaxRecommendedStops { get; set; } = 5;
}