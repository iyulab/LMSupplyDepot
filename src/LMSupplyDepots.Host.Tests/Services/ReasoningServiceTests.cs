using Xunit;
using LMSupplyDepots.Host.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LMSupplyDepots.Host.Tests.Services;

/// <summary>
/// Tests for reasoning service
/// </summary>
public class ReasoningServiceTests
{
    private readonly IReasoningService _reasoningService;

    public ReasoningServiceTests()
    {
        var logger = NullLogger<ReasoningService>.Instance;
        _reasoningService = new ReasoningService(logger);
    }

    #region IsReasoningResponse Tests

    [Fact]
    public void IsReasoningResponse_WithThinkingTags_ShouldReturnTrue()
    {
        // Arrange
        var content = "<thinking>Let me think about this...</thinking>\nThe answer is 42.";

        // Act
        var result = _reasoningService.IsReasoningResponse(content);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsReasoningResponse_WithReasoningTags_ShouldReturnTrue()
    {
        // Arrange
        var content = "<reasoning>I need to analyze this step by step...</reasoning>\nThe solution is X.";

        // Act
        var result = _reasoningService.IsReasoningResponse(content);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsReasoningResponse_WithInternalTags_ShouldReturnTrue()
    {
        // Arrange
        var content = "<internal>Internal thought process...</internal>\nFinal response here.";

        // Act
        var result = _reasoningService.IsReasoningResponse(content);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsReasoningResponse_WithThoughtTags_ShouldReturnTrue()
    {
        // Arrange
        var content = "<thought>Considering the options...</thought>\nI recommend option A.";

        // Act
        var result = _reasoningService.IsReasoningResponse(content);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsReasoningResponse_WithMarkdownThinking_ShouldReturnTrue()
    {
        // Arrange
        var content = "**Thinking:** This is a complex problem...\n**Answer:** The solution is Y.";

        // Act
        var result = _reasoningService.IsReasoningResponse(content);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsReasoningResponse_WithLetMeThink_ShouldReturnTrue()
    {
        // Arrange
        var content = "Let me think about this...\nAfter consideration, the answer is Z.";

        // Act
        var result = _reasoningService.IsReasoningResponse(content);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsReasoningResponse_WithoutReasoningPatterns_ShouldReturnFalse()
    {
        // Arrange
        var content = "This is a simple direct answer without any reasoning patterns.";

        // Act
        var result = _reasoningService.IsReasoningResponse(content);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsReasoningResponse_WithEmptyString_ShouldReturnFalse()
    {
        // Act
        var result = _reasoningService.IsReasoningResponse(string.Empty);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsReasoningResponse_WithNull_ShouldReturnFalse()
    {
        // Act
        var result = _reasoningService.IsReasoningResponse(null);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region ExtractThinking Tests

    [Fact]
    public void ExtractThinking_WithThinkingTags_ShouldExtractCorrectly()
    {
        // Arrange
        var content = "<thinking>This is my internal reasoning process.</thinking>\nThe answer is 42.";
        var expected = "This is my internal reasoning process.";

        // Act
        var result = _reasoningService.ExtractThinking(content);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ExtractThinking_WithReasoningTags_ShouldExtractCorrectly()
    {
        // Arrange
        var content = "<reasoning>Step 1: Analyze\nStep 2: Decide</reasoning>\nMy decision is X.";
        var expected = "Step 1: Analyze\nStep 2: Decide";

        // Act
        var result = _reasoningService.ExtractThinking(content);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ExtractThinking_WithMultiplePatterns_ShouldExtractFirst()
    {
        // Arrange
        var content = "<thinking>First thinking</thinking>\n<reasoning>Second reasoning</reasoning>\nAnswer";
        var expected = "First thinking";

        // Act
        var result = _reasoningService.ExtractThinking(content);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ExtractThinking_WithMarkdownThinking_ShouldExtractCorrectly()
    {
        // Arrange
        var content = "**Thinking:** I need to consider multiple factors here.\n**Answer:** The best option is A.";
        var expected = " I need to consider multiple factors here.";

        // Act
        var result = _reasoningService.ExtractThinking(content);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ExtractThinking_WithNoThinking_ShouldReturnEmpty()
    {
        // Arrange
        var content = "This is just a regular response without thinking.";

        // Act
        var result = _reasoningService.ExtractThinking(content);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ExtractThinking_WithEmptyContent_ShouldReturnEmpty()
    {
        // Act
        var result = _reasoningService.ExtractThinking(string.Empty);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    #endregion

    #region ExtractFinalAnswer Tests

    [Fact]
    public void ExtractFinalAnswer_WithAnswerTag_ShouldExtractCorrectly()
    {
        // Arrange
        var content = "<thinking>Some reasoning...</thinking>\n<answer>The final answer is 42.</answer>";
        var expected = "The final answer is 42.";

        // Act
        var result = _reasoningService.ExtractFinalAnswer(content);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ExtractFinalAnswer_WithMarkdownAnswer_ShouldExtractCorrectly()
    {
        // Arrange
        var content = "**Thinking:** Complex reasoning...\n**Answer:** The solution is X.";
        var expected = " The solution is X.";

        // Act
        var result = _reasoningService.ExtractFinalAnswer(content);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ExtractFinalAnswer_WithThereforePattern_ShouldExtractCorrectly()
    {
        // Arrange
        var content = "<thinking>Analysis...</thinking>\nTherefore, the answer is Y.";
        var expected = "the answer is Y.";

        // Act
        var result = _reasoningService.ExtractFinalAnswer(content);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ExtractFinalAnswer_WithoutExplicitAnswer_ShouldRemoveThinkingContent()
    {
        // Arrange
        var content = "<thinking>Internal reasoning...</thinking>\nThis is the final response.";
        var expected = "This is the final response.";

        // Act
        var result = _reasoningService.ExtractFinalAnswer(content);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ExtractFinalAnswer_WithOnlyThinking_ShouldReturnEmpty()
    {
        // Arrange
        var content = "<thinking>Only thinking, no final answer</thinking>";

        // Act
        var result = _reasoningService.ExtractFinalAnswer(content);

        // Assert
        Assert.Equal(string.Empty, result.Trim());
    }

    [Fact]
    public void ExtractFinalAnswer_WithNoThinking_ShouldReturnOriginal()
    {
        // Arrange
        var content = "This is just a direct answer without any thinking.";

        // Act
        var result = _reasoningService.ExtractFinalAnswer(content);

        // Assert
        Assert.Equal(content, result);
    }

    #endregion

    #region CountReasoningTokens Tests

    [Fact]
    public void CountReasoningTokens_WithShortText_ShouldReturnMinimumTokens()
    {
        // Arrange
        var thinkingContent = "Short";

        // Act
        var result = _reasoningService.CountReasoningTokens(thinkingContent);

        // Assert
        Assert.True(result >= 3); // Minimum 1 token + 2 for tags
    }

    [Fact]
    public void CountReasoningTokens_WithLongerText_ShouldCalculateApproximately()
    {
        // Arrange
        var thinkingContent = "This is a longer piece of thinking content that should result in more tokens being counted.";

        // Act
        var result = _reasoningService.CountReasoningTokens(thinkingContent);

        // Assert
        Assert.True(result > 10); // Should be more than 10 tokens for this length
        Assert.True(result < 50); // But reasonable upper bound
    }

    [Fact]
    public void CountReasoningTokens_WithEmptyContent_ShouldReturnZero()
    {
        // Act
        var result = _reasoningService.CountReasoningTokens(string.Empty);

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public void CountReasoningTokens_WithNull_ShouldReturnZero()
    {
        // Act
        var result = _reasoningService.CountReasoningTokens(null);

        // Assert
        Assert.Equal(0, result);
    }

    #endregion

    #region ProcessReasoningAsync Tests

    [Fact]
    public async Task ProcessReasoningAsync_WithThinkingContent_ShouldProcessCorrectly()
    {
        // Arrange
        var content = "<thinking>Let me analyze this step by step.\n1. Consider option A\n2. Consider option B</thinking>\nBased on my analysis, option A is better.";
        var expectedThinking = "Let me analyze this step by step.\n1. Consider option A\n2. Consider option B";
        var expectedAnswer = "Based on my analysis, option A is better.";

        // Act
        var result = await _reasoningService.ProcessReasoningAsync(content);

        // Assert
        Assert.True(result.HasReasoning);
        Assert.Equal(expectedThinking, result.ThinkingContent);
        Assert.Equal(expectedAnswer, result.FinalAnswer);
        Assert.True(result.ReasoningTokens > 0);
        Assert.Equal(content, result.OriginalContent);
    }

    [Fact]
    public async Task ProcessReasoningAsync_WithoutThinkingContent_ShouldTreatAsDirectAnswer()
    {
        // Arrange
        var content = "This is a direct answer without any thinking patterns.";

        // Act
        var result = await _reasoningService.ProcessReasoningAsync(content);

        // Assert
        Assert.False(result.HasReasoning);
        Assert.Equal(string.Empty, result.ThinkingContent);
        Assert.Equal(content, result.FinalAnswer);
        Assert.Equal(0, result.ReasoningTokens);
        Assert.Equal(content, result.OriginalContent);
    }

    [Fact]
    public async Task ProcessReasoningAsync_WithEmptyContent_ShouldReturnEmptyResult()
    {
        // Act
        var result = await _reasoningService.ProcessReasoningAsync(string.Empty);

        // Assert
        Assert.False(result.HasReasoning);
        Assert.Equal(string.Empty, result.ThinkingContent);
        Assert.Equal(string.Empty, result.FinalAnswer);
        Assert.Equal(0, result.ReasoningTokens);
        Assert.Equal(string.Empty, result.OriginalContent);
    }

    [Fact]
    public async Task ProcessReasoningAsync_WithNullContent_ShouldReturnEmptyResult()
    {
        // Act
        var result = await _reasoningService.ProcessReasoningAsync(null);

        // Assert
        Assert.False(result.HasReasoning);
        Assert.Equal(string.Empty, result.ThinkingContent);
        Assert.Equal(string.Empty, result.FinalAnswer);
        Assert.Equal(0, result.ReasoningTokens);
        Assert.Equal(string.Empty, result.OriginalContent);
    }

    [Fact]
    public async Task ProcessReasoningAsync_WithComplexReasoningPattern_ShouldHandleCorrectly()
    {
        // Arrange
        var content = @"<thinking>
This is a complex problem that requires multiple steps:

1. First, I need to understand the requirements
2. Then analyze the available options
3. Consider the trade-offs
4. Make a recommendation

Let me work through each step:

Step 1: The user wants a solution that is both fast and reliable
Step 2: Options are A, B, and C
Step 3: A is fast but unreliable, B is slow but reliable, C is balanced
Step 4: C seems like the best choice
</thinking>

After careful consideration, I recommend option C as it provides the best balance between speed and reliability.";

        // Act
        var result = await _reasoningService.ProcessReasoningAsync(content);

        // Assert
        Assert.True(result.HasReasoning);
        Assert.Contains("This is a complex problem", result.ThinkingContent);
        Assert.Contains("Step 1:", result.ThinkingContent);
        Assert.Contains("Step 4:", result.ThinkingContent);
        Assert.StartsWith("After careful consideration", result.FinalAnswer);
        Assert.True(result.ReasoningTokens > 50); // Should be substantial tokens for this content
    }

    [Theory]
    [InlineData("<thinking>Internal thought</thinking>Answer", true)]
    [InlineData("<reasoning>Analysis</reasoning>Result", true)]
    [InlineData("<internal>Process</internal>Output", true)]
    [InlineData("<thought>Consider</thought>Decision", true)]
    [InlineData("**Thinking:** Process\n**Answer:** Result", true)]
    [InlineData("Let me think about this...\nConclusion", true)]
    [InlineData("Direct answer without reasoning", false)]
    [InlineData("", false)]
    public async Task ProcessReasoningAsync_WithVariousPatterns_ShouldDetectCorrectly(string content, bool expectedHasReasoning)
    {
        // Act
        var result = await _reasoningService.ProcessReasoningAsync(content);

        // Assert
        Assert.Equal(expectedHasReasoning, result.HasReasoning);
    }

    #endregion
}