using Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using LMSupplyDepots.External.LLamaEngine.Services;

namespace LMSupplyDepots.External.LLamaEngine.Tests.Services;

/// <summary>
/// Tests for the StopTokenOptimizer service
/// </summary>
public class StopTokenOptimizerTests
{
    private readonly IStopTokenOptimizer _optimizer;

    public StopTokenOptimizerTests()
    {
        var logger = NullLogger<StopTokenOptimizer>.Instance;
        _optimizer = new StopTokenOptimizer(logger);
    }

    #region OptimizeStopTokens Tests

    [Fact]
    public void OptimizeStopTokens_LlamaArchitecture_ReturnsOptimizedTokens()
    {
        // Arrange
        var architecture = "llama";
        var requestTokens = new List<string> { "\n", "User:" };
        var context = new ModelOptimizationContext
        {
            Strategy = StopTokenStrategy.Balanced,
            ExpectedLength = GenerationLength.Medium,
            SupportsToolCalling = false
        };

        // Act
        var result = _optimizer.OptimizeStopTokens(architecture, requestTokens, context);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.PrimaryStopTokens.Count > 0);
        Assert.Contains("<|eot_id|>", result.PrimaryStopTokens);
        Assert.DoesNotContain("\n", result.GetAllStopTokens()); // Should be filtered as problematic
        Assert.Contains("\n", result.FilteredStopTokens); // Should be in filtered list
    }

    [Fact]
    public void OptimizeStopTokens_Phi3Architecture_FiltersProblematicTokens()
    {
        // Arrange
        var architecture = "phi3";
        var requestTokens = new List<string> { "\n", "<|assistant|>", "User:" };
        var context = new ModelOptimizationContext
        {
            Strategy = StopTokenStrategy.Conservative,
            ExpectedLength = GenerationLength.Short
        };

        // Act
        var result = _optimizer.OptimizeStopTokens(architecture, requestTokens, context);

        // Assert
        Assert.Contains("<|end|>", result.PrimaryStopTokens);
        Assert.DoesNotContain("\n", result.GetAllStopTokens());
        Assert.DoesNotContain("<|assistant|>", result.GetAllStopTokens());
        Assert.True(result.FilteredStopTokens.Count >= 2); // \n and <|assistant|> should be filtered
    }

    [Fact]
    public void OptimizeStopTokens_ToolCallingContext_AddsToolStops()
    {
        // Arrange
        var architecture = "mistral";
        var requestTokens = new List<string> { "User:" };
        var context = new ModelOptimizationContext
        {
            SupportsToolCalling = true,
            Strategy = StopTokenStrategy.Balanced
        };

        // Act
        var result = _optimizer.OptimizeStopTokens(architecture, requestTokens, context);

        // Assert
        // For mistral with tool calling, at least one tool token should be present
        // Due to MaxRecommendedStops limit, both tokens might not be included
        var allTokens = result.GetAllStopTokens();
        Assert.True(allTokens.Contains("[TOOL_CALLS]") || allTokens.Contains("[/TOOL_CALLS]"),
            $"At least one tool calling token should be present. All tokens: {string.Join(", ", allTokens)}");
    }

    [Fact]
    public void OptimizeStopTokens_ConservativeStrategy_AddsMoreSafetyStops()
    {
        // Arrange
        var architecture = "llama";
        var requestTokens = new List<string>();
        var contextConservative = new ModelOptimizationContext { Strategy = StopTokenStrategy.Conservative };
        var contextPermissive = new ModelOptimizationContext { Strategy = StopTokenStrategy.Permissive };

        // Act
        var conservativeResult = _optimizer.OptimizeStopTokens(architecture, requestTokens, contextConservative);
        var permissiveResult = _optimizer.OptimizeStopTokens(architecture, requestTokens, contextPermissive);

        // Assert
        Assert.True(conservativeResult.SecondaryStopTokens.Count > permissiveResult.SecondaryStopTokens.Count);
    }

    [Fact]
    public void OptimizeStopTokens_ShortGeneration_AddsAgressiveStops()
    {
        // Arrange
        var architecture = "qwen";
        var requestTokens = new List<string>();
        var context = new ModelOptimizationContext
        {
            ExpectedLength = GenerationLength.Short,
            Strategy = StopTokenStrategy.Balanced
        };

        // Act
        var result = _optimizer.OptimizeStopTokens(architecture, requestTokens, context);

        // Assert
        Assert.Contains("\n\n", result.ContextStopTokens); // Should add aggressive stops for short generation
    }

    #endregion

    #region ValidateStopTokens Tests

    [Fact]
    public void ValidateStopTokens_ValidTokens_ReturnsValid()
    {
        // Arrange
        var architecture = "llama";
        var validTokens = new List<string> { "</s>", "User:", "Human:" };

        // Act
        var result = _optimizer.ValidateStopTokens(architecture, validTokens);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues.Where(i => i.Severity >= IssueSeverity.Error));
    }

    [Fact]
    public void ValidateStopTokens_ProblematicTokens_ReturnsInvalid()
    {
        // Arrange
        var architecture = "phi3";
        var problematicTokens = new List<string> { "\n", "<|assistant|>", "<|end|>" };

        // Act
        var result = _optimizer.ValidateStopTokens(architecture, problematicTokens);

        // Assert
        Assert.False(result.IsValid);
        Assert.True(result.Issues.Count >= 2); // \n and <|assistant|> should have issues
        Assert.Contains(result.Issues, i => i.StopToken == "\n" && i.Severity >= IssueSeverity.Error);
        Assert.Contains(result.Issues, i => i.StopToken == "<|assistant|>" && i.Severity >= IssueSeverity.Error);
    }

    [Fact]
    public void ValidateStopTokens_OverlyBroadTokens_ReturnsWarnings()
    {
        // Arrange
        var architecture = "mistral";
        var broadTokens = new List<string> { "a", ".", "1" };

        // Act
        var result = _optimizer.ValidateStopTokens(architecture, broadTokens);

        // Assert
        Assert.True(result.Issues.All(i => i.Severity == IssueSeverity.Warning));
        Assert.True(result.Issues.Count >= 3); // All should have warnings for being overly broad
    }

    #endregion

    #region GetRecommendedStopTokens Tests

    [Fact]
    public void GetRecommendedStopTokens_LlamaModel_ReturnsAppropriateTokens()
    {
        // Arrange
        var architecture = "llama";
        var modelName = "llama-3.2-7b-instruct";
        var context = new ModelOptimizationContext
        {
            Strategy = StopTokenStrategy.Balanced,
            SupportsToolCalling = false
        };

        // Act
        var result = _optimizer.GetRecommendedStopTokens(architecture, modelName, context);

        // Assert
        Assert.Contains("<|eot_id|>", result);
        Assert.Contains("</s>", result);
        Assert.True(result.Count <= 5); // Should respect max recommended stops
    }

    [Fact]
    public void GetRecommendedStopTokens_InstructModel_AddsUserStops()
    {
        // Arrange
        var architecture = "phi3";
        var modelName = "phi-3.5-mini-instruct";
        var context = new ModelOptimizationContext
        {
            Strategy = StopTokenStrategy.Balanced
        };

        // Act
        var result = _optimizer.GetRecommendedStopTokens(architecture, modelName, context);

        // Assert
        // phi3 with Balanced strategy should include primary stops + first 2 safety stops
        Assert.Contains("<|end|>", result);  // Primary stop
        Assert.Contains("<|user|>", result); // First safety stop
        Assert.Contains("<|system|>", result); // Second safety stop
    }

    [Fact]
    public void GetRecommendedStopTokens_CodeModel_AddsCodeStops()
    {
        // Arrange
        var architecture = "deepseek";
        var modelName = "deepseek-coder-7b";
        var context = new ModelOptimizationContext
        {
            SupportsToolCalling = true
        };

        // Act
        var result = _optimizer.GetRecommendedStopTokens(architecture, modelName, context);

        // Assert
        Assert.Contains("```", result);
    }

    #endregion

    #region DetectConflicts Tests

    [Fact]
    public void DetectConflicts_PrematureStop_DetectsConflict()
    {
        // Arrange
        var generatedText = "The user said 'User: hello' and then continued with more text";
        var appliedStops = new List<string> { "User:" };

        // Act
        var conflicts = _optimizer.DetectConflicts(generatedText, appliedStops);

        // Assert
        Assert.Single(conflicts);
        Assert.Equal(ConflictType.PrematureStop, conflicts[0].Type);
        Assert.Equal("User:", conflicts[0].StopToken);
    }

    [Fact]
    public void DetectConflicts_TemplateInterference_DetectsConflict()
    {
        // Arrange
        var generatedText = "Response <|start_header_id|>assistant";
        var appliedStops = new List<string> { "<|start_header_id|>" };

        // Act
        var conflicts = _optimizer.DetectConflicts(generatedText, appliedStops);

        // Assert
        Assert.Single(conflicts);
        Assert.Equal(ConflictType.TemplateInterference, conflicts[0].Type);
    }

    [Fact]
    public void DetectConflicts_NoConflicts_ReturnsEmpty()
    {
        // Arrange
        var generatedText = "This is a clean response without any conflicts.";
        var appliedStops = new List<string> { "</s>", "\nUser:" };

        // Act
        var conflicts = _optimizer.DetectConflicts(generatedText, appliedStops);

        // Assert
        Assert.Empty(conflicts);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void OptimizeStopTokens_UnknownArchitecture_UsesDefaultFallback()
    {
        // Arrange
        var architecture = "unknown_arch";
        var requestTokens = new List<string> { "User:" };
        var context = new ModelOptimizationContext();

        // Act
        var result = _optimizer.OptimizeStopTokens(architecture, requestTokens, context);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.GetAllStopTokens().Count > 0); // Should fallback to llama defaults
    }

    [Fact]
    public void OptimizeStopTokens_EmptyRequestTokens_StillReturnsDefaults()
    {
        // Arrange
        var architecture = "mistral";
        var requestTokens = new List<string>();
        var context = new ModelOptimizationContext();

        // Act
        var result = _optimizer.OptimizeStopTokens(architecture, requestTokens, context);

        // Assert
        Assert.True(result.PrimaryStopTokens.Count > 0);
        Assert.Contains("</s>", result.PrimaryStopTokens);
    }

    [Fact]
    public void OptimizeStopTokens_ExcessiveStopTokens_LimitsToMaximum()
    {
        // Arrange
        var architecture = "phi3";
        var requestTokens = new List<string> { "stop1", "stop2", "stop3", "stop4", "stop5", "stop6", "stop7", "stop8" };
        var context = new ModelOptimizationContext { Strategy = StopTokenStrategy.Conservative };

        // Act
        var result = _optimizer.OptimizeStopTokens(architecture, requestTokens, context);

        // Assert
        Assert.True(result.GetAllStopTokens().Count <= 8); // Should limit to reasonable maximum
        Assert.True(result.FilteredStopTokens.Count > 0); // Some should be filtered due to limit
    }

    #endregion
}