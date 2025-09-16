using LMSupplyDepots.Host.Controllers;
using LMSupplyDepots.Host.Services;
using LMSupplyDepots.SDK.OpenAI.Models;
using LMSupplyDepots.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace LMSupplyDepots.Host.Tests.Controllers;

/// <summary>
/// Enhanced tests for V1Controller functionality
/// </summary>
public class V1ControllerEnhancedTests
{
    private readonly Mock<IHostService> _mockHostService;
    private readonly Mock<IToolExecutionService> _mockToolExecutionService;
    private readonly Mock<IDynamicToolService> _mockDynamicToolService;
    private readonly Mock<ILogger<V1Controller>> _mockLogger;
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly V1Controller _controller;

    public V1ControllerEnhancedTests()
    {
        _mockHostService = new Mock<IHostService>();
        _mockToolExecutionService = new Mock<IToolExecutionService>();
        _mockDynamicToolService = new Mock<IDynamicToolService>();
        _mockLogger = new Mock<ILogger<V1Controller>>();
        _mockServiceProvider = new Mock<IServiceProvider>();

        _controller = new V1Controller(
            _mockHostService.Object,
            _mockToolExecutionService.Object,
            _mockDynamicToolService.Object,
            _mockLogger.Object,
            _mockServiceProvider.Object);
    }

    #region Enhanced Chat Completion Tests

    [Fact]
    public async Task CreateChatCompletion_HandlesMultipleContentTypes_Successfully()
    {
        // Arrange
        var model = CreateTestModel("test-model", ModelType.TextGeneration);
        
        var request = new OpenAIChatCompletionRequest
        {
            Model = "test-model",
            Messages = new List<OpenAIChatMessage>
            {
                new OpenAIChatMessage { Role = "system", Content = new TextContentPart { Text = "You are a helpful assistant" } },
                new OpenAIChatMessage { Role = "user", Content = new TextContentPart { Text = "Hello" } }
            },
            MaxCompletionTokens = 50,
            Temperature = 0.7f
        };

        var responseObject = new OpenAIChatCompletionResponse
        {
            Id = "chatcmpl-123",
            Object = "chat.completion",
            Created = 1234567890,
            Model = "test-model",
            Choices = new List<OpenAIChatChoice>
            {
                new OpenAIChatChoice
                {
                    Index = 0,
                    Message = new OpenAIChatMessage { Role = "assistant", Content = new TextContentPart { Text = "Hello! How can I help you today?" } },
                    FinishReason = "stop"
                }
            }
        };

        _mockHostService.Setup(x => x.CreateChatCompletionAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseObject);

        // Act
        var result = await _controller.CreateChatCompletion(request, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task CreateChatCompletion_ReturnsBadRequest_WhenModelIsMissing()
    {
        // Arrange
        var request = new OpenAIChatCompletionRequest
        {
            Model = "", // Empty model
            Messages = new List<OpenAIChatMessage>
            {
                new OpenAIChatMessage { Role = "user", Content = new TextContentPart { Text = "Hello" } }
            }
        };

        // Act
        var result = await _controller.CreateChatCompletion(request, CancellationToken.None);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequestResult.Value);
    }

    [Fact]
    public async Task CreateChatCompletion_ReturnsBadRequest_WhenMessagesAreEmpty()
    {
        // Arrange
        var request = new OpenAIChatCompletionRequest
        {
            Model = "test-model",
            Messages = new List<OpenAIChatMessage>() // Empty messages
        };

        // Act
        var result = await _controller.CreateChatCompletion(request, CancellationToken.None);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequestResult.Value);
    }

    [Fact]
    public async Task CreateChatCompletion_ReturnsBadRequest_WhenRoleIsInvalid()
    {
        // Arrange
        var request = new OpenAIChatCompletionRequest
        {
            Model = "test-model",
            Messages = new List<OpenAIChatMessage>
            {
                new OpenAIChatMessage { Role = "invalid_role", Content = new TextContentPart { Text = "Hello" } }
            }
        };

        // Act
        var result = await _controller.CreateChatCompletion(request, CancellationToken.None);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequestResult.Value);
    }

    [Fact]
    public async Task CreateChatCompletion_ReturnsBadRequest_WhenTemperatureIsOutOfRange()
    {
        // Arrange
        var request = new OpenAIChatCompletionRequest
        {
            Model = "test-model",
            Messages = new List<OpenAIChatMessage>
            {
                new OpenAIChatMessage { Role = "user", Content = new TextContentPart { Text = "Hello" } }
            },
            Temperature = 3.0f // Invalid temperature > 2
        };

        // Act
        var result = await _controller.CreateChatCompletion(request, CancellationToken.None);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequestResult.Value);
    }

    [Fact]
    public async Task CreateEmbeddings_ReturnsValidOpenAIResponse_WhenRequestIsValid()
    {
        // Arrange
        var model = CreateTestModel("embedding-model", ModelType.Embedding, supportsEmbeddings: true);
        
        var request = new OpenAIEmbeddingRequest
        {
            Model = "embedding-model",
            Input = new List<string> { "Hello world" }
        };

        var responseObject = new OpenAIEmbeddingResponse
        {
            Object = "list",
            Model = "embedding-model",
            Data = new List<OpenAIEmbeddingData>
            {
                new OpenAIEmbeddingData
                {
                    Object = "embedding",
                    Index = 0,
                    Embedding = new[] { 0.1f, 0.2f, 0.3f }
                }
            }
        };

        _mockHostService.Setup(x => x.CreateEmbeddingsAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseObject);

        // Act
        var result = await _controller.CreateEmbeddings(request, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task CreateEmbeddings_ReturnsBadRequest_WhenInputIsMissing()
    {
        // Arrange
        var request = new OpenAIEmbeddingRequest
        {
            Model = "embedding-model",
            Input = null! // Missing input
        };

        // Act
        var result = await _controller.CreateEmbeddings(request, CancellationToken.None);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequestResult.Value);
    }

    #endregion

    #region Helper Methods

    private static LMModel CreateTestModel(string id, ModelType type, bool supportsEmbeddings = false)
    {
        return new LMModel
        {
            Id = id,
            Name = $"Test {id}",
            Type = type,
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
