using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using LMSupplyDepots.Host.Controllers;
using LMSupplyDepots.Host;
using LMSupplyDepots.Host.Services;
using LMSupplyDepots.Models;
using LMSupplyDepots.Contracts;
using LMSupplyDepots.Host.Models.OpenAI;
using Xunit;
using System.Text.Json;

namespace LMSupplyDepots.Host.Tests.Controllers;

/// <summary>
/// Enhanced tests for OpenAI API compliance with updated models
/// </summary>
public class V1ControllerEnhancedTests
{
    private readonly Mock<IHostService> _mockHostService;
    private readonly Mock<IOpenAIConverterService> _mockConverterService;
    private readonly Mock<ILogger<V1Controller>> _mockLogger;
    private readonly V1Controller _controller;

    public V1ControllerEnhancedTests()
    {
        _mockHostService = new Mock<IHostService>();
        _mockConverterService = new Mock<IOpenAIConverterService>();
        _mockLogger = new Mock<ILogger<V1Controller>>();
        _controller = new V1Controller(_mockHostService.Object, _mockConverterService.Object, _mockLogger.Object);
    }

    #region Enhanced Chat Completion Tests

    [Fact]
    public async Task CreateChatCompletion_HandlesMultipleContentTypes_Successfully()
    {
        // Arrange
        var model = CreateTestModel("test-model", true, ModelType.TextGeneration);
        var request = new OpenAIChatCompletionRequest
        {
            Model = "test-model",
            Messages = new List<OpenAIChatMessage>
            {
                new() { Role = "system", Content = new TextContentPart { Text = "You are a helpful assistant" } },
                new() { Role = "user", Content = new TextContentPart { Text = "Hello" } }
            },
            MaxCompletionTokens = 50,
            Temperature = 0.7f
        };

        var generationResponse = new GenerationResponse
        {
            Text = "Hello! How can I help you today?",
            FinishReason = "stop",
            PromptTokens = 8,
            OutputTokens = 10
        };

        _mockHostService.Setup(x => x.GetModelAsync("test-model", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);
        _mockHostService.Setup(x => x.GenerateTextAsync("test-model", It.IsAny<GenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(generationResponse);

        // Act
        var result = await _controller.CreateChatCompletion(request, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<OpenAIChatCompletionResponse>(okResult.Value);

        Assert.StartsWith("chatcmpl-", response.Id);
        Assert.Equal("chat.completion", response.Object);
        Assert.Equal("test-model", response.Model);
        Assert.Single(response.Choices);
        Assert.Equal(8, response.Usage.PromptTokens);
        Assert.Equal(10, response.Usage.CompletionTokens);
        Assert.Equal(18, response.Usage.TotalTokens);
    }

    [Fact]
    public async Task CreateChatCompletion_ValidatesToolMessages_RequireToolCallId()
    {
        // Arrange
        var request = new OpenAIChatCompletionRequest
        {
            Model = "test-model",
            Messages = new List<OpenAIChatMessage>
            {
                new() { Role = "tool", Content = new TextContentPart { Text = "Function result" } }
                // Missing ToolCallId
            }
        };

        // Act
        var result = await _controller.CreateChatCompletion(request, CancellationToken.None);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        var errorResponse = Assert.IsType<OpenAIErrorResponse>(badRequestResult.Value);

        Assert.Equal("invalid_request_error", errorResponse.Error.Type);
        Assert.Contains("tool_call_id", errorResponse.Error.Message);
        Assert.Equal("messages", errorResponse.Error.Param);
    }

    [Fact]
    public async Task CreateChatCompletion_AcceptsDeveloperRole_AsSystemReplacement()
    {
        // Arrange
        var model = CreateTestModel("test-model", true, ModelType.TextGeneration);
        var request = new OpenAIChatCompletionRequest
        {
            Model = "test-model",
            Messages = new List<OpenAIChatMessage>
            {
                new() { Role = "developer", Content = new TextContentPart { Text = "You are a coding assistant" } },
                new() { Role = "user", Content = new TextContentPart { Text = "Write a function" } }
            }
        };

        var generationResponse = new GenerationResponse
        {
            Text = "Here's a function for you",
            FinishReason = "stop",
            PromptTokens = 10,
            OutputTokens = 6
        };

        _mockHostService.Setup(x => x.GetModelAsync("test-model", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);
        _mockHostService.Setup(x => x.GenerateTextAsync("test-model", It.IsAny<GenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(generationResponse);

        // Act
        var result = await _controller.CreateChatCompletion(request, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<OpenAIChatCompletionResponse>(okResult.Value);

        Assert.Equal("stop", response.Choices[0].FinishReason);
        Assert.Equal("Here's a function for you", response.Choices[0].Message.Content);
    }

    [Fact]
    public async Task CreateChatCompletion_ValidatesMaxCompletionTokens_InsteadOfDeprecatedMaxTokens()
    {
        // Arrange
        var request = new OpenAIChatCompletionRequest
        {
            Model = "test-model",
            Messages = new List<OpenAIChatMessage>
            {
                new() { Role = "user", Content = new TextContentPart { Text = "Hello" } }
            },
            MaxCompletionTokens = -1 // Invalid value
        };

        // Act
        var result = await _controller.CreateChatCompletion(request, CancellationToken.None);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        var errorResponse = Assert.IsType<OpenAIErrorResponse>(badRequestResult.Value);

        Assert.Equal("invalid_request_error", errorResponse.Error.Type);
        Assert.Contains("Max completion tokens must be greater than 0", errorResponse.Error.Message);
        Assert.Equal("max_completion_tokens", errorResponse.Error.Param);
    }

    [Fact]
    public async Task CreateChatCompletion_HandlesStopSequences_WithImplicitConversion()
    {
        // Arrange
        var model = CreateTestModel("test-model", true, ModelType.TextGeneration);
        var request = new OpenAIChatCompletionRequest
        {
            Model = "test-model",
            Messages = new List<OpenAIChatMessage>
            {
                new() { Role = "user", Content = new TextContentPart { Text = "Count to 10" } }
            },
            Stop = new List<string> { "\n", "." }
        };

        var generationResponse = new GenerationResponse
        {
            Text = "1, 2, 3",
            FinishReason = "stop",
            PromptTokens = 5,
            OutputTokens = 6
        };

        _mockHostService.Setup(x => x.GetModelAsync("test-model", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);
        _mockHostService.Setup(x => x.GenerateTextAsync("test-model", It.IsAny<GenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(generationResponse);

        // Act
        var result = await _controller.CreateChatCompletion(request, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);

        // Verify that GenerateTextAsync was called with stop parameters
        _mockHostService.Verify(x => x.GenerateTextAsync("test-model",
            It.Is<GenerationRequest>(req =>
                req.Parameters.ContainsKey("stop")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateChatCompletion_AllowsAssistantWithoutContent_WhenToolCallsPresent()
    {
        // Arrange
        var model = CreateTestModel("test-model", true, ModelType.TextGeneration);
        var request = new OpenAIChatCompletionRequest
        {
            Model = "test-model",
            Messages = new List<OpenAIChatMessage>
            {
                new() { Role = "user", Content = new TextContentPart { Text = "What's the weather?" } },
                new()
                {
                    Role = "assistant",
                    Content = null, // No content but has tool calls
                    ToolCalls = new List<ToolCall>
                    {
                        new()
                        {
                            Id = "call_123",
                            Type = "function",
                            Function = new FunctionCall { Name = "get_weather", Arguments = "{\"location\":\"NYC\"}" }
                        }
                    }
                },
                new()
                {
                    Role = "tool",
                    Content = new TextContentPart { Text = "Sunny, 75°F" },
                    ToolCallId = "call_123"
                }
            }
        };

        var generationResponse = new GenerationResponse
        {
            Text = "The weather in NYC is sunny and 75°F.",
            FinishReason = "stop",
            PromptTokens = 25,
            OutputTokens = 12
        };

        _mockHostService.Setup(x => x.GetModelAsync("test-model", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);
        _mockHostService.Setup(x => x.GenerateTextAsync("test-model", It.IsAny<GenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(generationResponse);

        // Act
        var result = await _controller.CreateChatCompletion(request, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<OpenAIChatCompletionResponse>(okResult.Value);

        Assert.Equal("The weather in NYC is sunny and 75°F.", response.Choices[0].Message.Content);
    }

    #endregion

    #region Enhanced Embeddings Tests

    [Fact]
    public async Task CreateEmbeddings_ReturnsValidResponse_WithUpdatedModel()
    {
        // Arrange
        var model = CreateTestModel("embedding-model", true, ModelType.Embedding, supportsEmbeddings: true);
        var request = new OpenAIEmbeddingRequest
        {
            Model = "embedding-model",
            Input = "Hello world",
            EncodingFormat = "float",
            Dimensions = 512
        };

        var embeddingResponse = new EmbeddingResponse
        {
            Embeddings = new List<float[]>
            {
                new float[] { 0.1f, 0.2f, 0.3f }
            },
            TotalTokens = 2
        };

        _mockHostService.Setup(x => x.GetModelAsync("embedding-model", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);
        _mockHostService.Setup(x => x.GenerateEmbeddingsAsync("embedding-model", It.IsAny<EmbeddingRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(embeddingResponse);

        // Act
        var result = await _controller.CreateEmbeddings(request, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<OpenAIEmbeddingResponse>(okResult.Value);

        Assert.Equal("list", response.Object);
        Assert.Equal("embedding-model", response.Model);
        Assert.Single(response.Data);
        Assert.Equal("embedding", response.Data[0].Object);
        Assert.Equal(new float[] { 0.1f, 0.2f, 0.3f }, response.Data[0].Embedding);
    }

    #endregion

    #region Helper Methods

    private static LMModel CreateTestModel(string id, bool isLoaded, ModelType type, bool supportsEmbeddings = false)
    {
        return new LMModel
        {
            Id = id,
            Name = $"Test {id}",
            Type = type,
            IsLoaded = isLoaded,
            Capabilities = new LMModelCapabilities
            {
                SupportsTextGeneration = type == ModelType.TextGeneration,
                SupportsEmbeddings = supportsEmbeddings || type == ModelType.Embedding,
                SupportsImageUnderstanding = false,
                MaxContextLength = 4096
            }
        };
    }

    #endregion
}
