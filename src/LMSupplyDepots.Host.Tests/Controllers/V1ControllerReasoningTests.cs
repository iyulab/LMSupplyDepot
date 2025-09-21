using Xunit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using LMSupplyDepots.Host.Controllers;
using LMSupplyDepots.Host.Services;
using LMSupplyDepots.SDK.OpenAI.Models;
using LMSupplyDepots.SDK.Services;
using System.Text.Json;

namespace LMSupplyDepots.Host.Tests.Controllers;

/// <summary>
/// Tests for V1Controller reasoning functionality
/// </summary>
public class V1ControllerReasoningTests
{
    private readonly Mock<IHostService> _mockHostService;
    private readonly Mock<IToolExecutionService> _mockToolExecutionService;
    private readonly Mock<IDynamicToolService> _mockDynamicToolService;
    private readonly IReasoningService _reasoningService;
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly V1Controller _controller;

    public V1ControllerReasoningTests()
    {
        _mockHostService = new Mock<IHostService>();
        _mockToolExecutionService = new Mock<IToolExecutionService>();
        _mockDynamicToolService = new Mock<IDynamicToolService>();
        _reasoningService = new ReasoningService(NullLogger<ReasoningService>.Instance);
        _mockServiceProvider = new Mock<IServiceProvider>();

        var logger = NullLogger<V1Controller>.Instance;
        _controller = new V1Controller(
            _mockHostService.Object,
            _mockToolExecutionService.Object,
            _mockDynamicToolService.Object,
            _reasoningService,
            logger,
            _mockServiceProvider.Object);
    }

    [Fact]
    public async Task CreateChatCompletion_WithThinkingContent_ShouldProcessReasoningAndReturnFinalAnswer()
    {
        // Arrange
        var request = new OpenAIChatCompletionRequest
        {
            Model = "test-model",
            Messages = new List<OpenAIChatMessage>
            {
                new OpenAIChatMessage
                {
                    Role = "user",
                    Content = new TextContentPart { Text = "What is 2+2?" }
                }
            }
        };

        var responseWithThinking = new OpenAIChatCompletionResponse
        {
            Id = "test-id",
            Object = "chat.completion",
            Created = 1640995200,
            Model = "test-model",
            Usage = new OpenAIUsage
            {
                PromptTokens = 10,
                CompletionTokens = 20,
                TotalTokens = 30
            },
            Choices = new List<OpenAIChatChoice>
            {
                new OpenAIChatChoice
                {
                    Index = 0,
                    Message = new OpenAIChatMessage
                    {
                        Role = "assistant",
                        Content = new TextContentPart
                        {
                            Text = "<thinking>Let me calculate 2+2. This is a simple addition problem. 2 plus 2 equals 4.</thinking>\nThe answer is 4."
                        }
                    },
                    FinishReason = "stop"
                }
            }
        };

        _mockHostService
            .Setup(s => s.CreateChatCompletionAsync(It.IsAny<OpenAIChatCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseWithThinking);

        // Act
        var result = await _controller.CreateChatCompletion(request, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<OpenAIChatCompletionResponse>(okResult.Value);

        // Verify the thinking content was processed
        var messageContent = response.Choices.First().Message.Content as TextContentPart;
        Assert.NotNull(messageContent);
        Assert.Equal("The answer is 4.", messageContent.Text);
        Assert.DoesNotContain("<thinking>", messageContent.Text);

        // Verify reasoning tokens were added to usage
        Assert.NotNull(response.Usage.ReasoningTokens);
        Assert.True(response.Usage.ReasoningTokens > 0);
        Assert.True(response.Usage.TotalTokens > 30); // Should be increased by reasoning tokens
    }

    [Fact]
    public async Task CreateChatCompletion_WithoutThinkingContent_ShouldNotProcessReasoning()
    {
        // Arrange
        var request = new OpenAIChatCompletionRequest
        {
            Model = "test-model",
            Messages = new List<OpenAIChatMessage>
            {
                new OpenAIChatMessage
                {
                    Role = "user",
                    Content = new TextContentPart { Text = "Hello" }
                }
            }
        };

        var directResponse = new OpenAIChatCompletionResponse
        {
            Id = "test-id",
            Object = "chat.completion",
            Created = 1640995200,
            Model = "test-model",
            Usage = new OpenAIUsage
            {
                PromptTokens = 5,
                CompletionTokens = 10,
                TotalTokens = 15
            },
            Choices = new List<OpenAIChatChoice>
            {
                new OpenAIChatChoice
                {
                    Index = 0,
                    Message = new OpenAIChatMessage
                    {
                        Role = "assistant",
                        Content = new TextContentPart { Text = "Hello! How can I help you?" }
                    },
                    FinishReason = "stop"
                }
            }
        };

        _mockHostService
            .Setup(s => s.CreateChatCompletionAsync(It.IsAny<OpenAIChatCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(directResponse);

        // Act
        var result = await _controller.CreateChatCompletion(request, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<OpenAIChatCompletionResponse>(okResult.Value);

        // Verify no reasoning processing occurred
        var messageContent = response.Choices.First().Message.Content as TextContentPart;
        Assert.NotNull(messageContent);
        Assert.Equal("Hello! How can I help you?", messageContent.Text);

        // Verify no reasoning tokens were added
        Assert.Null(response.Usage.ReasoningTokens);
        Assert.Equal(15, response.Usage.TotalTokens); // Should remain unchanged
    }

    [Fact]
    public async Task CreateChatCompletion_WithComplexReasoningPattern_ShouldHandleCorrectly()
    {
        // Arrange
        var request = new OpenAIChatCompletionRequest
        {
            Model = "test-model",
            Messages = new List<OpenAIChatMessage>
            {
                new OpenAIChatMessage
                {
                    Role = "user",
                    Content = new TextContentPart { Text = "Solve this complex problem" }
                }
            }
        };

        var complexReasoningResponse = new OpenAIChatCompletionResponse
        {
            Id = "test-id",
            Object = "chat.completion",
            Created = 1640995200,
            Model = "test-model",
            Usage = new OpenAIUsage
            {
                PromptTokens = 20,
                CompletionTokens = 100,
                TotalTokens = 120
            },
            Choices = new List<OpenAIChatChoice>
            {
                new OpenAIChatChoice
                {
                    Index = 0,
                    Message = new OpenAIChatMessage
                    {
                        Role = "assistant",
                        Content = new TextContentPart
                        {
                            Text = @"<reasoning>
This is a complex problem that requires multiple steps:

1. First, I need to analyze the requirements
2. Then consider various approaches
3. Evaluate trade-offs
4. Make a recommendation

Let me work through this systematically:

Step 1: The problem involves optimization under constraints
Step 2: We could use method A, B, or C
Step 3: Method A is fast but inaccurate, B is slow but precise, C is balanced
Step 4: Given the requirements, method C is optimal
</reasoning>

After careful analysis, I recommend using method C as it provides the best balance of speed and accuracy for this type of problem."
                        }
                    },
                    FinishReason = "stop"
                }
            }
        };

        _mockHostService
            .Setup(s => s.CreateChatCompletionAsync(It.IsAny<OpenAIChatCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(complexReasoningResponse);

        // Act
        var result = await _controller.CreateChatCompletion(request, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<OpenAIChatCompletionResponse>(okResult.Value);

        // Verify the reasoning content was processed correctly
        var messageContent = response.Choices.First().Message.Content as TextContentPart;
        Assert.NotNull(messageContent);
        Assert.StartsWith("After careful analysis", messageContent.Text);
        Assert.DoesNotContain("<reasoning>", messageContent.Text);
        Assert.DoesNotContain("Step 1:", messageContent.Text);

        // Verify substantial reasoning tokens were counted
        Assert.NotNull(response.Usage.ReasoningTokens);
        Assert.True(response.Usage.ReasoningTokens > 20); // Should be substantial for complex reasoning
        Assert.True(response.Usage.TotalTokens > 120); // Should be increased significantly
    }

    [Theory]
    [InlineData("<thinking>Simple thought</thinking>Answer")]
    [InlineData("<reasoning>Analysis here</reasoning>Conclusion")]
    [InlineData("<internal>Internal process</internal>Output")]
    [InlineData("<thought>Consideration</thought>Decision")]
    [InlineData("**Thinking:** Process here\n**Answer:** Final result")]
    public async Task CreateChatCompletion_WithVariousReasoningPatterns_ShouldProcessCorrectly(string responseText)
    {
        // Arrange
        var request = new OpenAIChatCompletionRequest
        {
            Model = "test-model",
            Messages = new List<OpenAIChatMessage>
            {
                new OpenAIChatMessage
                {
                    Role = "user",
                    Content = new TextContentPart { Text = "Test question" }
                }
            }
        };

        var mockResponse = new OpenAIChatCompletionResponse
        {
            Id = "test-id",
            Object = "chat.completion",
            Created = 1640995200,
            Model = "test-model",
            Usage = new OpenAIUsage
            {
                PromptTokens = 10,
                CompletionTokens = 20,
                TotalTokens = 30
            },
            Choices = new List<OpenAIChatChoice>
            {
                new OpenAIChatChoice
                {
                    Index = 0,
                    Message = new OpenAIChatMessage
                    {
                        Role = "assistant",
                        Content = new TextContentPart { Text = responseText }
                    },
                    FinishReason = "stop"
                }
            }
        };

        _mockHostService
            .Setup(s => s.CreateChatCompletionAsync(It.IsAny<OpenAIChatCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        // Act
        var result = await _controller.CreateChatCompletion(request, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<OpenAIChatCompletionResponse>(okResult.Value);

        // Verify reasoning was processed
        var messageContent = response.Choices.First().Message.Content as TextContentPart;
        Assert.NotNull(messageContent);

        // Content should not contain thinking tags
        Assert.DoesNotContain("<thinking>", messageContent.Text);
        Assert.DoesNotContain("<reasoning>", messageContent.Text);
        Assert.DoesNotContain("<internal>", messageContent.Text);
        Assert.DoesNotContain("<thought>", messageContent.Text);

        // Should have reasoning tokens
        Assert.NotNull(response.Usage.ReasoningTokens);
        Assert.True(response.Usage.ReasoningTokens > 0);
    }

    [Fact]
    public async Task CreateChatCompletion_WithEmptyResponse_ShouldHandleGracefully()
    {
        // Arrange
        var request = new OpenAIChatCompletionRequest
        {
            Model = "test-model",
            Messages = new List<OpenAIChatMessage>
            {
                new OpenAIChatMessage
                {
                    Role = "user",
                    Content = new TextContentPart { Text = "Test question" }
                }
            }
        };

        var emptyResponse = new OpenAIChatCompletionResponse
        {
            Id = "test-id",
            Object = "chat.completion",
            Created = 1640995200,
            Model = "test-model",
            Usage = new OpenAIUsage
            {
                PromptTokens = 10,
                CompletionTokens = 0,
                TotalTokens = 10
            },
            Choices = new List<OpenAIChatChoice>
            {
                new OpenAIChatChoice
                {
                    Index = 0,
                    Message = new OpenAIChatMessage
                    {
                        Role = "assistant",
                        Content = new TextContentPart { Text = "" }
                    },
                    FinishReason = "stop"
                }
            }
        };

        _mockHostService
            .Setup(s => s.CreateChatCompletionAsync(It.IsAny<OpenAIChatCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyResponse);

        // Act
        var result = await _controller.CreateChatCompletion(request, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<OpenAIChatCompletionResponse>(okResult.Value);

        // Should handle empty response without errors
        Assert.NotNull(response);
        Assert.Equal(10, response.Usage.TotalTokens); // Should remain unchanged
        Assert.Null(response.Usage.ReasoningTokens); // No reasoning tokens for empty content
    }

    [Fact]
    public async Task CreateChatCompletion_WithNullMessageContent_ShouldHandleGracefully()
    {
        // Arrange
        var request = new OpenAIChatCompletionRequest
        {
            Model = "test-model",
            Messages = new List<OpenAIChatMessage>
            {
                new OpenAIChatMessage
                {
                    Role = "user",
                    Content = new TextContentPart { Text = "Test question" }
                }
            }
        };

        var nullContentResponse = new OpenAIChatCompletionResponse
        {
            Id = "test-id",
            Object = "chat.completion",
            Created = 1640995200,
            Model = "test-model",
            Usage = new OpenAIUsage
            {
                PromptTokens = 10,
                CompletionTokens = 0,
                TotalTokens = 10
            },
            Choices = new List<OpenAIChatChoice>
            {
                new OpenAIChatChoice
                {
                    Index = 0,
                    Message = new OpenAIChatMessage
                    {
                        Role = "assistant",
                        Content = null
                    },
                    FinishReason = "stop"
                }
            }
        };

        _mockHostService
            .Setup(s => s.CreateChatCompletionAsync(It.IsAny<OpenAIChatCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(nullContentResponse);

        // Act
        var result = await _controller.CreateChatCompletion(request, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<OpenAIChatCompletionResponse>(okResult.Value);

        // Should handle null content without errors
        Assert.NotNull(response);
        Assert.Equal(10, response.Usage.TotalTokens); // Should remain unchanged
        Assert.Null(response.Usage.ReasoningTokens); // No reasoning tokens for null content
    }
}