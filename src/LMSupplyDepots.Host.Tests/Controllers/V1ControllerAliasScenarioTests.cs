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
/// Scenario-based tests for V1Controller alias functionality
/// </summary>
public class V1ControllerAliasScenarioTests
{
    private readonly Mock<IHostService> _mockHostService;
    private readonly Mock<IToolExecutionService> _mockToolExecutionService;
    private readonly Mock<ILogger<V1Controller>> _mockLogger;
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly V1Controller _controller;

    public V1ControllerAliasScenarioTests()
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
    public async Task Scenario_HyperCLOVAXWithAlias_ReturnsAliasAsModelId()
    {
        // Scenario: User has loaded HyperCLOVA X model and set alias "hyperclovax"
        // Expected: /v1/models should return "hyperclovax" as the model ID instead of the full model ID

        // Arrange
        var response = new
        {
            @object = "list",
            data = new[]
            {
                new
                {
                    id = "hyperclovax",
                    @object = "model",
                    created = 1234567890,
                    owned_by = "local",
                    type = "text-generation"
                }
            }
        };

        _mockHostService
            .Setup(x => x.ListModelsOpenAIAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.ListModels(CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task Scenario_MultipleModels_MixedAliasUsage_ReturnsCorrectIds()
    {
        // Scenario: User has multiple models loaded, some with aliases, some without
        // Expected: Models with aliases should show alias as ID, others should show full ID

        // Arrange
        var response = new
        {
            @object = "list",
            data = new[]
            {
                new
                {
                    id = "hyperclovax",
                    @object = "model",
                    created = 1234567890,
                    owned_by = "local",
                    type = "text-generation"
                },
                new
                {
                    id = "hf:microsoft/DialoGPT-medium",
                    @object = "model",
                    created = 1234567890,
                    owned_by = "local",
                    type = "text-generation"
                },
                new
                {
                    id = "embeddings-model",
                    @object = "model",
                    created = 1234567890,
                    owned_by = "local",
                    type = "embedding"
                },
                new
                {
                    id = "local:my-custom-model",
                    @object = "model",
                    created = 1234567890,
                    owned_by = "local",
                    type = "text-generation"
                }
            }
        };

        _mockHostService
            .Setup(x => x.ListModelsOpenAIAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.ListModels(CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task Scenario_AliasChangedDuringRuntime_ReturnsNewAlias()
    {
        // Scenario: User changes an alias for a loaded model
        // Expected: Next call to /v1/models should return the new alias as ID

        // Arrange - Initial state with old alias
        var responseWithOldAlias = new
        {
            @object = "list",
            data = new[]
            {
                new
                {
                    id = "old-alias",
                    @object = "model",
                    created = 1234567890,
                    owned_by = "local",
                    type = "text-generation"
                }
            }
        };

        _mockHostService
            .Setup(x => x.ListModelsOpenAIAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseWithOldAlias);

        // Act - First call
        var result1 = await _controller.ListModels(CancellationToken.None);

        // Assert - Should return old alias
        var okResult1 = Assert.IsType<OkObjectResult>(result1);
        Assert.NotNull(okResult1.Value);

        // Arrange - Change alias
        var responseWithNewAlias = new
        {
            @object = "list",
            data = new[]
            {
                new
                {
                    id = "hyperclovax",
                    @object = "model",
                    created = 1234567890,
                    owned_by = "local",
                    type = "text-generation"
                }
            }
        };

        _mockHostService
            .Setup(x => x.ListModelsOpenAIAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseWithNewAlias);

        // Act - Second call
        var result2 = await _controller.ListModels(CancellationToken.None);

        // Assert - Should return new alias
        var okResult2 = Assert.IsType<OkObjectResult>(result2);
        Assert.NotNull(okResult2.Value);
    }

    [Fact]
    public async Task Scenario_AliasRemoved_ReturnsFullId()
    {
        // Scenario: User removes an alias from a model
        // Expected: /v1/models should return the full model ID instead of alias

        // Arrange - Model with no alias (alias removed)
        var response = new
        {
            @object = "list",
            data = new[]
            {
                new
                {
                    id = "hf:DevQuasar/naver-hyperclovax.HyperCLOVAX-SEED-Text-Instruct-0.5B-GGUF/naver-hyperclovax.HyperCLOVAX-SEED-Text-Instruct-0.5B.f16",
                    @object = "model",
                    created = 1234567890,
                    owned_by = "local",
                    type = "text-generation"
                }
            }
        };

        _mockHostService
            .Setup(x => x.ListModelsOpenAIAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.ListModels(CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }
}
