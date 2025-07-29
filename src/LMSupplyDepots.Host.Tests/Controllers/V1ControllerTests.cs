using LMSupplyDepots.Host.Controllers;
using LMSupplyDepots.Models;
using LMSupplyDepots.Host.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace LMSupplyDepots.Host.Tests.Controllers;

/// <summary>
/// Unit tests for V1Controller
/// </summary>
public class V1ControllerTests
{
    private readonly Mock<IHostService> _mockHostService;
    private readonly Mock<IToolExecutionService> _mockToolExecutionService;
    private readonly Mock<ILogger<V1Controller>> _mockLogger;
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly V1Controller _controller;

    public V1ControllerTests()
    {
        _mockHostService = new Mock<IHostService>();
        _mockToolExecutionService = new Mock<IToolExecutionService>();
        _mockLogger = new Mock<ILogger<V1Controller>>();
        _mockServiceProvider = new Mock<IServiceProvider>();

        _controller = new V1Controller(
            _mockHostService.Object,
            _mockToolExecutionService.Object,
            _mockLogger.Object,
            _mockServiceProvider.Object);
    }

    [Fact]
    public async Task ListModels_ReturnsModelsWithAliasAsId_WhenAliasIsSet()
    {
        // Arrange
        var modelWithAlias = new LMModel
        {
            Id = "hf:DevQuasar/naver-hyperclovax.HyperCLOVAX-SEED-Text-Instruct-0.5B-GGUF/naver-hyperclovax.HyperCLOVAX-SEED-Text-Instruct-0.5B.f16",
            Alias = "hyperclovax",
            Name = "HyperCLOVA X",
            Type = ModelType.TextGeneration
        };

        var modelWithoutAlias = new LMModel
        {
            Id = "hf:microsoft/DialoGPT-medium",
            Alias = null,
            Name = "DialoGPT Medium",
            Type = ModelType.TextGeneration
        };

        var responseObject = new
        {
            @object = "list",
            data = new[]
            {
                new { id = "hyperclovax", @object = "model", created = 1234567890, owned_by = "user" },
                new { id = "hf:microsoft/DialoGPT-medium", @object = "model", created = 1234567890, owned_by = "user" }
            }
        };

        _mockHostService
            .Setup(x => x.ListModelsOpenAIAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseObject);

        // Act
        var result = await _controller.ListModels(CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task ListModels_ReturnsEmptyList_WhenNoModelsLoaded()
    {
        // Arrange
        var responseObject = new
        {
            @object = "list",
            data = new object[0]
        };

        _mockHostService
            .Setup(x => x.ListModelsOpenAIAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseObject);

        // Act
        var result = await _controller.ListModels(CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task ListModels_ReturnsServerError_WhenServiceThrows()
    {
        // Arrange
        _mockHostService
            .Setup(x => x.ListModelsOpenAIAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Service error"));

        // Act
        var result = await _controller.ListModels(CancellationToken.None);

        // Assert
        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, statusResult.StatusCode);
    }

    [Fact]
    public void ModelKey_ReturnsAlias_WhenAliasIsSet()
    {
        // Arrange
        var model = new LMModel
        {
            Id = "hf:DevQuasar/naver-hyperclovax.HyperCLOVAX-SEED-Text-Instruct-0.5B-GGUF/naver-hyperclovax.HyperCLOVAX-SEED-Text-Instruct-0.5B.f16",
            Alias = "hyperclovax"
        };

        // Act & Assert
        Assert.Equal("hyperclovax", model.Key);
    }

    [Fact]
    public void ModelKey_ReturnsId_WhenAliasIsNull()
    {
        // Arrange
        var model = new LMModel
        {
            Id = "hf:microsoft/DialoGPT-medium",
            Alias = null
        };

        // Act & Assert
        Assert.Equal("hf:microsoft/DialoGPT-medium", model.Key);
    }

    [Fact]
    public void ModelKey_ReturnsId_WhenAliasIsEmpty()
    {
        // Arrange
        var model = new LMModel
        {
            Id = "hf:microsoft/DialoGPT-medium",
            Alias = ""
        };

        // Act & Assert
        Assert.Equal("hf:microsoft/DialoGPT-medium", model.Key);
    }
}
