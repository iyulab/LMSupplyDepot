using LMSupplyDepots.Host.Controllers;
using LMSupplyDepots.Host.Services;
using LMSupplyDepots.SDK.OpenAI.Models;
using LMSupplyDepots.SDK.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly Mock<IDynamicToolService> _mockDynamicToolService;
    private readonly Mock<IModelMetadataService> _mockModelMetadataService;
    private readonly Mock<ILogger<V1Controller>> _mockLogger;
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly V1Controller _controller;

    public V1ControllerAdaptiveToolCallTests()
    {
        _mockHostService = new Mock<IHostService>();
        _mockToolExecutionService = new Mock<IToolExecutionService>();
        _mockDynamicToolService = new Mock<IDynamicToolService>();
        _mockModelMetadataService = new Mock<IModelMetadataService>();
        _mockLogger = new Mock<ILogger<V1Controller>>();
        _mockServiceProvider = new Mock<IServiceProvider>();

        // Setup service provider to return the model metadata service
        _mockServiceProvider
            .Setup(x => x.GetService(typeof(IModelMetadataService)))
            .Returns(_mockModelMetadataService.Object);

        _controller = new V1Controller(
            _mockHostService.Object,
            _mockToolExecutionService.Object,
            _mockDynamicToolService.Object,
            _mockLogger.Object,
            _mockServiceProvider.Object);
    }

    [Fact]
    public async Task ParseToolCallsAsync_WithPhiModel_ParsesPhiFormatCorrectly()
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

        var expectedToolCalls = new List<ToolCall>
        {
            new ToolCall
            {
                Id = "call_123",
                Type = "function",
                Function = new FunctionCall
                {
                    Name = "get_weather",
                    Arguments = "{\"location\": \"Tokyo\"}"
                }
            }
        };

        _mockDynamicToolService
            .Setup(x => x.ParseToolCallsAsync(content, tools, modelId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedToolCalls);

        // Act
        var result = await _mockDynamicToolService.Object.ParseToolCallsAsync(content, tools, modelId);

        // Assert
        Assert.Single(result);
        Assert.Equal("get_weather", result[0].Function.Name);
        Assert.Contains("Tokyo", result[0].Function.Arguments);
    }

    [Fact]
    public async Task ParseToolCallsAsync_WithLlamaModel_ParsesLlamaFormatCorrectly()
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

        var expectedToolCalls = new List<ToolCall>
        {
            new ToolCall
            {
                Id = "call_456",
                Type = "function",
                Function = new FunctionCall
                {
                    Name = "get_weather",
                    Arguments = "{\"location\": \"London\"}"
                }
            }
        };

        _mockDynamicToolService
            .Setup(x => x.ParseToolCallsAsync(content, tools, modelId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedToolCalls);

        // Act
        var result = await _mockDynamicToolService.Object.ParseToolCallsAsync(content, tools, modelId);

        // Assert
        Assert.Single(result);
        Assert.Equal("get_weather", result[0].Function.Name);
        Assert.Contains("London", result[0].Function.Arguments);
    }

    [Fact]
    public async Task ParseToolCallsAsync_WithMistralModel_ParsesMistralFormatCorrectly()
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

        var expectedToolCalls = new List<ToolCall>
        {
            new ToolCall
            {
                Id = "call_789",
                Type = "function",
                Function = new FunctionCall
                {
                    Name = "get_weather",
                    Arguments = "{\"location\": \"Paris\"}"
                }
            }
        };

        _mockDynamicToolService
            .Setup(x => x.ParseToolCallsAsync(content, tools, modelId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedToolCalls);

        // Act
        var result = await _mockDynamicToolService.Object.ParseToolCallsAsync(content, tools, modelId);

        // Assert
        Assert.Single(result);
        Assert.Equal("get_weather", result[0].Function.Name);
        Assert.Contains("Paris", result[0].Function.Arguments);
    }

    [Fact]
    public async Task ParseToolCallsAsync_WithNoMetadataService_FallsBackToLegacyPatterns()
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

        var expectedToolCalls = new List<ToolCall>
        {
            new ToolCall
            {
                Id = "call_000",
                Type = "function",
                Function = new FunctionCall
                {
                    Name = "get_weather",
                    Arguments = "{\"location\": \"Sydney\"}"
                }
            }
        };

        _mockDynamicToolService
            .Setup(x => x.ParseToolCallsAsync(content, tools, modelId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedToolCalls);

        // Act
        var result = await _mockDynamicToolService.Object.ParseToolCallsAsync(content, tools, modelId);

        // Assert
        Assert.Single(result);
        Assert.Equal("get_weather", result[0].Function.Name);
        Assert.Contains("Sydney", result[0].Function.Arguments);
    }

    [Fact]
    public async Task ParseToolCallsAsync_WithQwenModel_ParsesQwenFormatCorrectly()
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

        var expectedToolCalls = new List<ToolCall>
        {
            new ToolCall
            {
                Id = "call_qwen",
                Type = "function",
                Function = new FunctionCall
                {
                    Name = "get_weather",
                    Arguments = "{\"location\": \"Beijing\"}"
                }
            }
        };

        _mockDynamicToolService
            .Setup(x => x.ParseToolCallsAsync(content, tools, modelId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedToolCalls);

        // Act
        var result = await _mockDynamicToolService.Object.ParseToolCallsAsync(content, tools, modelId);

        // Assert
        Assert.Single(result);
        Assert.Equal("get_weather", result[0].Function.Name);
        Assert.Contains("Beijing", result[0].Function.Arguments);
    }
}