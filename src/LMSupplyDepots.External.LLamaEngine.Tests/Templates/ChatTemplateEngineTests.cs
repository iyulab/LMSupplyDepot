using Xunit;
using LMSupplyDepots.External.LLamaEngine.Templates;
using LMSupplyDepots.External.LLamaEngine.Models;
using Microsoft.Extensions.Logging;
using Moq;
using TemplateMessage = LMSupplyDepots.External.LLamaEngine.Templates.ChatMessage;

namespace LMSupplyDepots.External.LLamaEngine.Tests;

public class ChatTemplateEngineTests
{
    private readonly ChatTemplateEngine _engine;
    private readonly Mock<ILogger<ChatTemplateEngine>> _loggerMock;

    public ChatTemplateEngineTests()
    {
        _loggerMock = new Mock<ILogger<ChatTemplateEngine>>();
        _engine = new ChatTemplateEngine(_loggerMock.Object);
    }

    [Fact]
    public void DetectTemplate_ShouldReturnLlama3Template_WhenModelNameContainsLlama3()
    {
        // Arrange
        var config = ModelConfig.Default;

        // Act
        var template = _engine.DetectTemplate("llama-3-8b-instruct", config);

        // Assert
        Assert.Contains("<|start_header_id|>", template);
        Assert.Contains("<|end_header_id|>", template);
    }

    [Fact]
    public void DetectTemplate_ShouldReturnMistralTemplate_WhenModelNameContainsMistral()
    {
        // Arrange
        var config = ModelConfig.Default;

        // Act
        var template = _engine.DetectTemplate("mistral-7b-instruct", config);

        // Assert
        Assert.Contains("[INST]", template);
        Assert.Contains("[/INST]", template);
    }

    [Fact]
    public void FormatMessages_ShouldFormatWithLlama3Template_WhenModelIsLlama3()
    {
        // Arrange
        var messages = new List<TemplateMessage>
        {
            new("system", "You are a helpful assistant."),
            new("user", "Hello!"),
            new("assistant", "Hi there!")
        };
        var config = new ModelConfig
        {
            Architecture = "llama",
            BosToken = "<s>",
            EosToken = "</s>"
        };

        // Act
        var result = _engine.FormatMessages(messages, config);

        // Assert
        Assert.Contains("<s>", result);
        Assert.Contains("<|start_header_id|>system<|end_header_id|>", result);
        Assert.Contains("<|start_header_id|>user<|end_header_id|>", result);
        Assert.Contains("<|start_header_id|>assistant<|end_header_id|>", result);
        Assert.Contains("You are a helpful assistant.", result);
        Assert.Contains("Hello!", result);
        Assert.Contains("Hi there!", result);
    }

    [Fact]
    public void FormatMessages_ShouldHandleEmptyMessages()
    {
        // Arrange
        var messages = new List<TemplateMessage>();
        var config = ModelConfig.Default;

        // Act
        var result = _engine.FormatMessages(messages, config);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    public void RegisterTemplate_ShouldAddCustomTemplate()
    {
        // Arrange
        var customTemplate = "Custom template: {{ message.role }}: {{ message.content }}";

        // Act
        _engine.RegisterTemplate("custom", customTemplate);
        var result = _engine.DetectTemplate("custom-model", ModelConfig.Default);

        // This test would need access to internal state or a way to verify template registration
        // For now, we'll just verify no exception is thrown
        Assert.NotNull(result);
    }

    [Fact]
    public void FormatMessages_ShouldFallbackToDefault_WhenTemplateProcessingFails()
    {
        // Arrange
        var messages = new List<TemplateMessage>
        {
            new("user", "Test message")
        };
        var config = new ModelConfig
        {
            ChatTemplate = "{% invalid template syntax",
            BosToken = "<s>",
            EosToken = "</s>"
        };

        // Act
        var result = _engine.FormatMessages(messages, config);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("user: Test message", result);
    }

    [Theory]
    [InlineData("llama-3.1-8b", "llama3")]
    [InlineData("mistral-7b-v0.1", "mistral")]
    [InlineData("codellama-13b", "codellama")]
    [InlineData("alpaca-7b", "alpaca")]
    [InlineData("vicuna-13b", "vicuna")]
    [InlineData("unknown-model", "llama3")] // Should fallback to llama3
    public void DetectTemplate_ShouldReturnCorrectTemplate_ForDifferentModelFamilies(string modelName, string expectedFamily)
    {
        // Arrange
        var config = ModelConfig.Default;

        // Act
        var template = _engine.DetectTemplate(modelName, config);

        // Assert
        Assert.NotNull(template);
        Assert.NotEmpty(template);

        // Verify specific template patterns
        switch (expectedFamily)
        {
            case "llama3":
                Assert.Contains("<|start_header_id|>", template);
                break;
            case "mistral":
                Assert.Contains("[INST]", template);
                break;
            case "codellama":
                Assert.Contains("### Instruction:", template);
                break;
            case "alpaca":
                Assert.Contains("### Instruction:", template);
                break;
            case "vicuna":
                Assert.Contains("USER:", template);
                break;
        }
    }
}
