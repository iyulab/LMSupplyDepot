using LMSupplyDepots.Host.Controllers;
using LMSupplyDepots.Host.Services;
using LMSupplyDepots.SDK.OpenAI.Models;
using LMSupplyDepots.SDK.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace LMSupplyDepots.Host.Tests.Controllers;

/// <summary>
/// Tests for adaptive tool call parsing in V1Controller
/// </summary>
public class V1ControllerAdaptiveToolCallTests
{
    private readonly Mock<IHostService> _mockHostService;
    private readonly Mock<IToolExecutionService> _mockToolExecutionService;
    private readonly Mock<IModelMetadataService> _mockModelMetadataService;
    private readonly Mock<ILogger<V1Controller>> _mockLogger;
    private readonly V1Controller _controller;

    public V1ControllerAdaptiveToolCallTests()
    {
        _mockHostService = new Mock<IHostService>();
        _mockToolExecutionService = new Mock<IToolExecutionService>();
        _mockModelMetadataService = new Mock<IModelMetadataService>();
        _mockLogger = new Mock<ILogger<V1Controller>>();

        _controller = new V1Controller(
            _mockHostService.Object,
            _mockToolExecutionService.Object,
            _mockModelMetadataService.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task ParseToolCallsFromContentAsync_WithPhiModel_ParsesPhiFormatCorrectly()
    {
        // Arrange
        var content = "<|tool|>{\"name\": \"get_weather\", \"parameters\": {\"location\": \"Tokyo\"}}<|/tool|>";
        var tools = new List<Tool>
        {
            new Tool
            {
                Type = "function",
                Function = new FunctionDefinition
                {
                    Name = "get_weather",
                    Description = "Get weather info"
                }
            }
        };
        var modelId = "phi-4-mini";

        var metadata = new ModelMetadata
        {
            Architecture = "phi3",
            ModelName = "phi-4-mini"
        };

        _mockModelMetadataService
            .Setup(x => x.GetModelMetadataAsync(modelId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(metadata);

        _mockModelMetadataService
            .Setup(x => x.GetToolCallFormatAsync(modelId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("<|tool|>{json}<|/tool|>");

        // Use reflection to access private method
        var method = typeof(V1Controller).GetMethod("ParseToolCallsFromContentAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // Act
        var result = await (Task<List<ToolCall>>)method!.Invoke(_controller, new object[] { content, tools, modelId })!;

        // Assert
        Assert.Single(result);
        Assert.Equal("get_weather", result[0].Function.Name);
        Assert.Contains("Tokyo", result[0].Function.Arguments);
    }

    [Fact]
    public async Task ParseToolCallsFromContentAsync_WithLlamaModel_ParsesLlamaFormatCorrectly()
    {
        // Arrange
        var content = "[TOOL_CALL] get_weather({\"location\": \"London\"}) [/TOOL_CALL]";
        var tools = new List<Tool>
        {
            new Tool
            {
                Type = "function",
                Function = new FunctionDefinition
                {
                    Name = "get_weather",
                    Description = "Get weather info"
                }
            }
        };
        var modelId = "llama-3.1";

        var metadata = new ModelMetadata
        {
            Architecture = "llama",
            ModelName = "llama-3.1"
        };

        _mockModelMetadataService
            .Setup(x => x.GetModelMetadataAsync(modelId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(metadata);

        _mockModelMetadataService
            .Setup(x => x.GetToolCallFormatAsync(modelId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("[TOOL_CALL] function_name(args) [/TOOL_CALL]");

        // Use reflection to access private method
        var method = typeof(V1Controller).GetMethod("ParseToolCallsFromContentAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // Act
        var result = await (Task<List<ToolCall>>)method!.Invoke(_controller, new object[] { content, tools, modelId })!;

        // Assert
        Assert.Single(result);
        Assert.Equal("get_weather", result[0].Function.Name);
        Assert.Contains("London", result[0].Function.Arguments);
    }

    [Fact]
    public async Task ParseToolCallsFromContentAsync_WithMistralModel_ParsesMistralFormatCorrectly()
    {
        // Arrange
        var content = "I'll help you with that. {\"tool_call\": {\"name\": \"get_weather\", \"args\": {\"location\": \"Paris\"}}}";
        var tools = new List<Tool>
        {
            new Tool
            {
                Type = "function",
                Function = new FunctionDefinition
                {
                    Name = "get_weather",
                    Description = "Get weather info"
                }
            }
        };
        var modelId = "mixtral-8x7b";

        var metadata = new ModelMetadata
        {
            Architecture = "mixtral",
            ModelName = "mixtral-8x7b"
        };

        _mockModelMetadataService
            .Setup(x => x.GetModelMetadataAsync(modelId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(metadata);

        _mockModelMetadataService
            .Setup(x => x.GetToolCallFormatAsync(modelId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"tool_call\": {\"name\": \"func\", \"args\": {}}}");

        // Use reflection to access private method
        var method = typeof(V1Controller).GetMethod("ParseToolCallsFromContentAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // Act
        var result = await (Task<List<ToolCall>>)method!.Invoke(_controller, new object[] { content, tools, modelId })!;

        // Assert
        Assert.Single(result);
        Assert.Equal("get_weather", result[0].Function.Name);
        Assert.Contains("Paris", result[0].Function.Arguments);
    }

    [Fact]
    public async Task ParseToolCallsFromContentAsync_WithNoMetadataService_FallsBackToLegacyPatterns()
    {
        // Arrange
        var content = "<tool_call>{\"name\": \"get_weather\", \"parameters\": {\"location\": \"Sydney\"}}</tool_call>";
        var tools = new List<Tool>
        {
            new Tool
            {
                Type = "function",
                Function = new FunctionDefinition
                {
                    Name = "get_weather",
                    Description = "Get weather info"
                }
            }
        };
        var modelId = "unknown-model";

        // Create controller without metadata service
        var controllerWithoutMetadata = new V1Controller(
            _mockHostService.Object,
            _mockToolExecutionService.Object,
            null, // No metadata service
            _mockLogger.Object);

        // Use reflection to access private method
        var method = typeof(V1Controller).GetMethod("ParseToolCallsFromContentAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // Act
        var result = await (Task<List<ToolCall>>)method!.Invoke(controllerWithoutMetadata, new object[] { content, tools, modelId })!;

        // Assert
        Assert.Single(result);
        Assert.Equal("get_weather", result[0].Function.Name);
        Assert.Contains("Sydney", result[0].Function.Arguments);
    }

    [Fact]
    public async Task ParseToolCallsFromContentAsync_WithQwenModel_ParsesQwenFormatCorrectly()
    {
        // Arrange
        var content = "<function_calls>[{\"name\": \"get_weather\", \"arguments\": {\"location\": \"Beijing\"}}]</function_calls>";
        var tools = new List<Tool>
        {
            new Tool
            {
                Type = "function",
                Function = new FunctionDefinition
                {
                    Name = "get_weather",
                    Description = "Get weather info"
                }
            }
        };
        var modelId = "qwen-2.5";

        var metadata = new ModelMetadata
        {
            Architecture = "qwen2",
            ModelName = "qwen-2.5"
        };

        _mockModelMetadataService
            .Setup(x => x.GetModelMetadataAsync(modelId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(metadata);

        _mockModelMetadataService
            .Setup(x => x.GetToolCallFormatAsync(modelId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("<function_calls>[{\"name\": \"func\", \"arguments\": {}}]</function_calls>");

        // Use reflection to access private method
        var method = typeof(V1Controller).GetMethod("ParseToolCallsFromContentAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // Act
        var result = await (Task<List<ToolCall>>)method!.Invoke(_controller, new object[] { content, tools, modelId })!;

        // Assert
        Assert.Single(result);
        Assert.Equal("get_weather", result[0].Function.Name);
        Assert.Contains("Beijing", result[0].Function.Arguments);
    }
}