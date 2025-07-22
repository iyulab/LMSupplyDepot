using Xunit;
using LMSupplyDepots.Host.Services;
using LMSupplyDepots.Host.Models.OpenAI;
using LMSupplyDepots.Models;
using LMSupplyDepots.Contracts;

namespace LMSupplyDepots.Host.Tests.Services;

/// <summary>
/// Tests for OpenAI converter service
/// </summary>
public class OpenAIConverterServiceTests
{
    private readonly OpenAIConverterService _converterService;

    public OpenAIConverterServiceTests()
    {
        _converterService = new OpenAIConverterService();
    }

    #region Model Conversion Tests

    [Fact]
    public void ConvertToOpenAIModel_ShouldConvertCorrectly()
    {
        // Arrange
        var model = new LMModel
        {
            Id = "test-model",
            Name = "Test Model",
            Type = ModelType.TextGeneration
        };
        var timestamp = 1640995200; // 2022-01-01 00:00:00 UTC

        // Act
        var result = _converterService.ConvertToOpenAIModel(model, timestamp);

        // Assert
        Assert.Equal(model.Key, result.Id);
        Assert.Equal("model", result.Object);
        Assert.Equal(timestamp, result.Created);
        Assert.Equal("local", result.OwnedBy);
        Assert.Equal("text-generation", result.Type);
    }

    [Fact]
    public void ConvertToOpenAIModel_ShouldHandleEmbeddingModel()
    {
        // Arrange
        var model = new LMModel
        {
            Id = "embedding-model",
            Name = "Embedding Model",
            Type = ModelType.Embedding
        };
        var timestamp = 1640995200;

        // Act
        var result = _converterService.ConvertToOpenAIModel(model, timestamp);

        // Assert
        Assert.Equal("embedding", result.Type);
    }

    #endregion

    #region Generation Request Conversion Tests

    [Fact]
    public void ConvertToGenerationRequest_ShouldConvertBasicRequest()
    {
        // Arrange
        var request = new OpenAIChatCompletionRequest
        {
            Model = "test-model",
            Messages = new List<OpenAIChatMessage>
            {
                new() { Role = "user", Content = new TextContentPart { Text = "Hello" } }
            },
            MaxCompletionTokens = 100,
            Temperature = 0.8f,
            TopP = 0.9f
        };

        // Act
        var result = _converterService.ConvertToGenerationRequest(request);

        // Assert
        Assert.Equal("test-model", result.Model);
        Assert.Equal(100, result.MaxTokens);
        Assert.Equal(0.8f, result.Temperature);
        Assert.Equal(0.9f, result.TopP);
        Assert.False(result.Stream);
        Assert.Contains("Hello", result.Prompt);
    }

    [Fact]
    public void ConvertToGenerationRequest_ShouldHandleStopSequences()
    {
        // Arrange
        var request = new OpenAIChatCompletionRequest
        {
            Model = "test-model",
            Messages = new List<OpenAIChatMessage>
            {
                new() { Role = "user", Content = new TextContentPart { Text = "Count to 10" } }
            },
            Stop = new List<string> { "\n", "." }
        };

        // Act
        var result = _converterService.ConvertToGenerationRequest(request);

        // Assert
        Assert.True(result.Parameters.ContainsKey("stop"));
        var stopSequences = result.Parameters["stop"] as List<string>;
        Assert.NotNull(stopSequences);
        Assert.Contains("\n", stopSequences);
        Assert.Contains(".", stopSequences);
    }

    [Fact]
    public void ConvertToGenerationRequest_ShouldHandleOptionalParameters()
    {
        // Arrange
        var request = new OpenAIChatCompletionRequest
        {
            Model = "test-model",
            Messages = new List<OpenAIChatMessage>
            {
                new() { Role = "user", Content = new TextContentPart { Text = "Hello" } }
            },
            PresencePenalty = 0.5f,
            FrequencyPenalty = 0.3f,
            Seed = 42,
            LogitBias = new Dictionary<string, int> { { "token1", 10 }, { "token2", -10 } }
        };

        // Act
        var result = _converterService.ConvertToGenerationRequest(request);

        // Assert
        Assert.Equal(0.5f, result.Parameters["presence_penalty"]);
        Assert.Equal(0.3f, result.Parameters["frequency_penalty"]);
        Assert.Equal(42, result.Parameters["seed"]);
        Assert.True(result.Parameters.ContainsKey("logit_bias"));
    }

    #endregion

    #region Response Conversion Tests

    [Fact]
    public void ConvertToOpenAIResponse_ShouldConvertCorrectly()
    {
        // Arrange
        var generationResponse = new GenerationResponse
        {
            Text = "Hello! How can I help you?",
            FinishReason = "stop",
            PromptTokens = 5,
            OutputTokens = 8
        };
        var completionId = "chatcmpl-123";
        var timestamp = 1640995200;
        var model = "test-model";

        // Act
        var result = _converterService.ConvertToOpenAIResponse(generationResponse, model, completionId, timestamp);

        // Assert
        Assert.Equal(completionId, result.Id);
        Assert.Equal("chat.completion", result.Object);
        Assert.Equal(timestamp, result.Created);
        Assert.Equal(model, result.Model);
        Assert.Single(result.Choices);

        var choice = result.Choices[0];
        Assert.Equal(0, choice.Index);
        Assert.Equal("assistant", choice.Message.Role);
        Assert.IsType<TextContentPart>(choice.Message.Content);
        Assert.Equal("Hello! How can I help you?", ((TextContentPart)choice.Message.Content!).Text);
        Assert.Equal("stop", choice.FinishReason);

        Assert.Equal(5, result.Usage.PromptTokens);
        Assert.Equal(8, result.Usage.CompletionTokens);
        Assert.Equal(13, result.Usage.TotalTokens);
    }

    [Fact]
    public void ConvertToOpenAIResponse_ShouldHandleDifferentFinishReasons()
    {
        // Arrange
        var generationResponse = new GenerationResponse
        {
            Text = "Truncated response",
            FinishReason = "length",
            PromptTokens = 5,
            OutputTokens = 100
        };

        // Act
        var result = _converterService.ConvertToOpenAIResponse(generationResponse, "test-model", "chatcmpl-123", 1640995200);

        // Assert
        Assert.Equal("length", result.Choices[0].FinishReason);
    }

    #endregion

    #region Embedding Conversion Tests

    [Fact]
    public void ConvertToEmbeddingRequest_ShouldConvertStringInput()
    {
        // Arrange
        var request = new OpenAIEmbeddingRequest
        {
            Model = "embedding-model",
            Input = "Hello world"
        };

        // Act
        var result = _converterService.ConvertToEmbeddingRequest(request);

        // Assert
        Assert.Equal("embedding-model", result.Model);
        Assert.Single(result.Texts);
        Assert.Equal("Hello world", result.Texts[0]);
        Assert.False(result.Normalize);
    }

    [Fact]
    public void ConvertToOpenAIEmbeddingResponse_ShouldConvertCorrectly()
    {
        // Arrange
        var embeddingResponse = new EmbeddingResponse
        {
            Embeddings = new List<float[]>
            {
                new float[] { 0.1f, 0.2f, 0.3f },
                new float[] { 0.4f, 0.5f, 0.6f }
            },
            TotalTokens = 4
        };
        var model = "embedding-model";

        // Act
        var result = _converterService.ConvertToOpenAIEmbeddingResponse(embeddingResponse, model);

        // Assert
        Assert.Equal("list", result.Object);
        Assert.Equal(model, result.Model);
        Assert.Equal(2, result.Data.Count);

        Assert.Equal(0, result.Data[0].Index);
        Assert.Equal("embedding", result.Data[0].Object);
        Assert.Equal(new float[] { 0.1f, 0.2f, 0.3f }, result.Data[0].Embedding);

        Assert.Equal(1, result.Data[1].Index);
        Assert.Equal(new float[] { 0.4f, 0.5f, 0.6f }, result.Data[1].Embedding);

        Assert.Equal(4, result.Usage.PromptTokens);
        Assert.Equal(4, result.Usage.TotalTokens);
    }

    #endregion

    #region Message Conversion Tests

    [Fact]
    public void ConvertMessagesToPrompt_ShouldHandleMultipleRoles()
    {
        // Arrange
        var messages = new List<OpenAIChatMessage>
        {
            new() { Role = "system", Content = new TextContentPart { Text = "You are helpful" } },
            new() { Role = "user", Content = new TextContentPart { Text = "Hello" } },
            new() { Role = "assistant", Content = new TextContentPart { Text = "Hi there!" } },
            new() { Role = "user", Content = new TextContentPart { Text = "How are you?" } }
        };

        // Act
        var result = _converterService.ConvertMessagesToPrompt(messages);

        // Assert
        Assert.Contains("System: You are helpful", result);
        Assert.Contains("User: Hello", result);
        Assert.Contains("Assistant: Hi there!", result);
        Assert.Contains("User: How are you?", result);
        Assert.EndsWith("\n\nAssistant:", result);
    }

    [Fact]
    public void ConvertMessagesToPrompt_ShouldHandleDeveloperRole()
    {
        // Arrange
        var messages = new List<OpenAIChatMessage>
        {
            new() { Role = "developer", Content = new TextContentPart { Text = "You are a coding assistant" } },
            new() { Role = "user", Content = new TextContentPart { Text = "Write a function" } }
        };

        // Act
        var result = _converterService.ConvertMessagesToPrompt(messages);

        // Assert
        Assert.Contains("System: You are a coding assistant", result);
        Assert.Contains("User: Write a function", result);
    }

    [Fact]
    public void ConvertMessagesToPrompt_ShouldHandleToolMessages()
    {
        // Arrange
        var messages = new List<OpenAIChatMessage>
        {
            new() { Role = "user", Content = new TextContentPart { Text = "What's the weather?" } },
            new()
            {
                Role = "assistant",
                Content = null,
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
        };

        // Act
        var result = _converterService.ConvertMessagesToPrompt(messages);

        // Assert
        Assert.Contains("User: What's the weather?", result);
        Assert.Contains("Assistant calls function: get_weather({\"location\":\"NYC\"})", result);
        Assert.Contains("Tool (call_123): Sunny, 75°F", result);
    }

    [Fact]
    public void ConvertMessagesToPrompt_ShouldHandleEmptyContent()
    {
        // Arrange
        var messages = new List<OpenAIChatMessage>
        {
            new() { Role = "user", Content = null },
            new() { Role = "assistant", Content = new TextContentPart { Text = "" } }
        };

        // Act
        var result = _converterService.ConvertMessagesToPrompt(messages);

        // Assert
        Assert.Contains("User: ", result);
        Assert.Contains("Assistant: ", result);
    }

    #endregion
}
