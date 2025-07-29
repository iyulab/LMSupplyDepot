using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using LMSupplyDepots.Host.Controllers;
using LMSupplyDepots.Models;
using LMSupplyDepots.Contracts;
using Xunit;
using LMSupplyDepots.SDK.OpenAI.Services;
using LMSupplyDepots.SDK.OpenAI.Models;

namespace LMSupplyDepots.Host.Tests.Controllers;

/// <summary>
/// Tests for OpenAI API compliance in V1Controller
/// </summary>
public class V1ControllerOpenAIComplianceTests
{
    private readonly Mock<IHostService> _mockHostService;
    private readonly Mock<IOpenAIConverterService> _mockConverterService;
    private readonly Mock<ILogger<V1Controller>> _mockLogger;
    private readonly V1Controller _controller;

    public V1ControllerOpenAIComplianceTests()
    {
        _mockHostService = new Mock<IHostService>();
        _mockConverterService = new Mock<IOpenAIConverterService>();
        _mockLogger = new Mock<ILogger<V1Controller>>();
        _controller = new V1Controller(_mockHostService.Object, _mockConverterService.Object, _mockLogger.Object);
    }

    #region Chat Completions Tests

    [Fact]
    public async Task CreateChatCompletion_ReturnsValidOpenAIResponse_WhenRequestIsValid()
    {
        // Arrange
        var model = CreateTestModel("test-model", true, ModelType.TextGeneration);
        var request = new OpenAIChatCompletionRequest
        {
            Model = "test-model",
            Messages = new List<OpenAIChatMessage>
            {
                new() { Role = "user", Content = "Hello" }
            },
            MaxCompletionTokens = 50,
            Temperature = 0.7f,
            TopP = 0.95f
        };

        var generationResponse = new GenerationResponse
        {
            Text = "Hello! How can I help you today?",
            FinishReason = "stop",
            PromptTokens = 3,
            OutputTokens = 8
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

        var choice = response.Choices[0];
        Assert.Equal(0, choice.Index);
        Assert.Equal("assistant", choice.Message.Role);
        Assert.Equal("Hello! How can I help you today?", choice.Message.Content);
        Assert.Equal("stop", choice.FinishReason);

        Assert.Equal(3, response.Usage.PromptTokens);
        Assert.Equal(8, response.Usage.CompletionTokens);
        Assert.Equal(11, response.Usage.TotalTokens);
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
                new() { Role = "user", Content = "Hello" }
            }
        };

        // Act
        var result = await _controller.CreateChatCompletion(request, CancellationToken.None);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        var errorResponse = Assert.IsType<OpenAIErrorResponse>(badRequestResult.Value);

        Assert.Equal("invalid_request_error", errorResponse.Error.Type);
        Assert.Equal("Model is required", errorResponse.Error.Message);
        Assert.Equal("model", errorResponse.Error.Param);
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
        var errorResponse = Assert.IsType<OpenAIErrorResponse>(badRequestResult.Value);

        Assert.Equal("invalid_request_error", errorResponse.Error.Type);
        Assert.Equal("Messages are required", errorResponse.Error.Message);
        Assert.Equal("messages", errorResponse.Error.Param);
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
                new() { Role = "invalid_role", Content = "Hello" }
            }
        };

        // Act
        var result = await _controller.CreateChatCompletion(request, CancellationToken.None);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        var errorResponse = Assert.IsType<OpenAIErrorResponse>(badRequestResult.Value);

        Assert.Equal("invalid_request_error", errorResponse.Error.Type);
        Assert.Contains("Invalid message role", errorResponse.Error.Message);
        Assert.Equal("messages", errorResponse.Error.Param);
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
                new() { Role = "user", Content = "Hello" }
            },
            Temperature = 3.0f // Invalid temperature > 2
        };

        // Act
        var result = await _controller.CreateChatCompletion(request, CancellationToken.None);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        var errorResponse = Assert.IsType<OpenAIErrorResponse>(badRequestResult.Value);

        Assert.Equal("invalid_request_error", errorResponse.Error.Type);
        Assert.Equal("Temperature must be between 0 and 2", errorResponse.Error.Message);
        Assert.Equal("temperature", errorResponse.Error.Param);
    }

    [Fact]
    public async Task CreateChatCompletion_ReturnsNotFound_WhenModelNotFound()
    {
        // Arrange
        var request = new OpenAIChatCompletionRequest
        {
            Model = "non-existent-model",
            Messages = new List<OpenAIChatMessage>
            {
                new() { Role = "user", Content = "Hello" }
            }
        };

        _mockHostService.Setup(x => x.GetModelAsync("non-existent-model", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LMModel?)null);

        // Act
        var result = await _controller.CreateChatCompletion(request, CancellationToken.None);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        var errorResponse = Assert.IsType<OpenAIErrorResponse>(notFoundResult.Value);

        Assert.Equal("model_not_found", errorResponse.Error.Type);
        Assert.Contains("not found", errorResponse.Error.Message);
        Assert.Equal("model", errorResponse.Error.Param);
    }

    [Fact]
    public async Task CreateChatCompletion_ReturnsNotFound_WhenModelNotLoaded()
    {
        // Arrange
        var model = CreateTestModel("test-model", false, ModelType.TextGeneration); // Not loaded
        var request = new OpenAIChatCompletionRequest
        {
            Model = "test-model",
            Messages = new List<OpenAIChatMessage>
            {
                new() { Role = "user", Content = "Hello" }
            }
        };

        _mockHostService.Setup(x => x.GetModelAsync("test-model", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        // Act
        var result = await _controller.CreateChatCompletion(request, CancellationToken.None);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        var errorResponse = Assert.IsType<OpenAIErrorResponse>(notFoundResult.Value);

        Assert.Equal("model_not_found", errorResponse.Error.Type);
        Assert.Contains("not loaded", errorResponse.Error.Message);
        Assert.Equal("model", errorResponse.Error.Param);
    }

    [Fact]
    public async Task CreateChatCompletion_IncludesStopSequences_WhenProvided()
    {
        // Arrange
        var model = CreateTestModel("test-model", true, ModelType.TextGeneration);
        var request = new OpenAIChatCompletionRequest
        {
            Model = "test-model",
            Messages = new List<OpenAIChatMessage>
            {
                new() { Role = "user", Content = "Count to 10" }
            },
            Stop = new List<string> { "\n", "5" }
        };

        var generationResponse = new GenerationResponse
        {
            Text = "1, 2, 3, 4,",
            FinishReason = "stop",
            PromptTokens = 5,
            OutputTokens = 8
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

    #endregion

    #region Embeddings Tests

    [Fact]
    public async Task CreateEmbeddings_ReturnsValidOpenAIResponse_WhenRequestIsValid()
    {
        // Arrange
        var model = CreateTestModel("embedding-model", true, ModelType.Embedding, supportsEmbeddings: true);
        var request = new OpenAIEmbeddingRequest
        {
            Model = "embedding-model",
            Input = "Hello world"
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

        var embedding = response.Data[0];
        Assert.Equal("embedding", embedding.Object);
        Assert.Equal(0, embedding.Index);
        Assert.Equal(new float[] { 0.1f, 0.2f, 0.3f }, embedding.Embedding);

        Assert.Equal(2, response.Usage.PromptTokens);
        Assert.Equal(2, response.Usage.TotalTokens);
    }

    [Fact]
    public async Task CreateEmbeddings_ReturnsBadRequest_WhenModelDoesNotSupportEmbeddings()
    {
        // Arrange
        var model = CreateTestModel("text-model", true, ModelType.TextGeneration, supportsEmbeddings: false);
        var request = new OpenAIEmbeddingRequest
        {
            Model = "text-model",
            Input = "Hello world"
        };

        _mockHostService.Setup(x => x.GetModelAsync("text-model", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        // Act
        var result = await _controller.CreateEmbeddings(request, CancellationToken.None);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        var errorResponse = Assert.IsType<OpenAIErrorResponse>(badRequestResult.Value);

        Assert.Equal("model_not_supported", errorResponse.Error.Type);
        Assert.Contains("does not support embeddings", errorResponse.Error.Message);
        Assert.Equal("model", errorResponse.Error.Param);
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
        var errorResponse = Assert.IsType<OpenAIErrorResponse>(badRequestResult.Value);

        Assert.Equal("invalid_request_error", errorResponse.Error.Type);
        Assert.Equal("Input is required", errorResponse.Error.Message);
        Assert.Equal("input", errorResponse.Error.Param);
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
