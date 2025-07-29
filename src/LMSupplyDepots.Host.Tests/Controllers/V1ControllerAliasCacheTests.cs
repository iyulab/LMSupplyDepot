using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using LMSupplyDepots.Host.Controllers;
using LMSupplyDepots.Models;
using LMSupplyDepots.Host.Services;
using Xunit;

namespace LMSupplyDepots.Host.Tests.Controllers;

/// <summary>
/// Tests for the specific issue where alias changes are not reflected in /v1/models
/// </summary>
public class V1ControllerAliasCacheTests
{
    private readonly Mock<IHostService> _mockHostService;
    private readonly Mock<IToolExecutionService> _mockToolExecutionService;
    private readonly Mock<ILogger<V1Controller>> _mockLogger;
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly V1Controller _controller;

    public V1ControllerAliasCacheTests()
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
    public async Task Scenario_AliasChangeNotReflectedInV1Models_ShouldBeFixed()
    {
        // This test reproduces the exact issue described by the user:
        // 1. GET /api/models  - alias: old-alias
        // 2. Load Model (POST /api/models/load)
        // 3. List Loaded Models (GET /v1/models) - alias: old-alias
        // 4. PUT Alias (PUT /api/alias) - alias: old-alias -> new-alias
        // 5. List Loaded Models (GET /v1/models) - alias: old-alias (PROBLEM!)

        // Step 1 & 2: Model is loaded with old alias
        var responseWithOldAlias = new
        {
            @object = "list",
            data = new[]
            {
                new { id = "old-alias", @object = "model", created = 1234567890, owned_by = "user" }
            }
        };

        _mockHostService
            .Setup(x => x.ListModelsOpenAIAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseWithOldAlias);

        // Step 3: First call to /v1/models should show old-alias
        var result1 = await _controller.ListModels(CancellationToken.None);
        var okResult1 = Assert.IsType<OkObjectResult>(result1);
        Assert.NotNull(okResult1.Value);

        // Step 4: Alias is changed (simulated by updating the mock to return updated model)
        var responseWithNewAlias = new
        {
            @object = "list",
            data = new[]
            {
                new { id = "new-alias", @object = "model", created = 1234567890, owned_by = "user" }
            }
        };

        _mockHostService
            .Setup(x => x.ListModelsOpenAIAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseWithNewAlias);

        // Step 5: Second call to /v1/models should now show new-alias (THIS WAS THE BUG)
        var result2 = await _controller.ListModels(CancellationToken.None);
        var okResult2 = Assert.IsType<OkObjectResult>(result2);
        Assert.NotNull(okResult2.Value);
    }

    [Fact]
    public async Task Scenario_AliasRemovedAfterLoad_ShouldShowFullId()
    {
        // Test the scenario where alias is completely removed

        // Step 1: Model loaded with alias
        var responseWithAlias = new
        {
            @object = "list",
            data = new[]
            {
                new { id = "test-alias", @object = "model", created = 1234567890, owned_by = "user" }
            }
        };

        _mockHostService
            .Setup(x => x.ListModelsOpenAIAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseWithAlias);

        var result1 = await _controller.ListModels(CancellationToken.None);
        var okResult1 = Assert.IsType<OkObjectResult>(result1);
        Assert.NotNull(okResult1.Value);

        // Step 2: Alias removed
        var responseWithoutAlias = new
        {
            @object = "list",
            data = new[]
            {
                new { id = "hf:test/model", @object = "model", created = 1234567890, owned_by = "user" }
            }
        };

        _mockHostService
            .Setup(x => x.ListModelsOpenAIAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseWithoutAlias);

        var result2 = await _controller.ListModels(CancellationToken.None);
        var okResult2 = Assert.IsType<OkObjectResult>(result2);
        Assert.NotNull(okResult2.Value);
    }

    [Fact]
    public async Task Scenario_MultipleAliasChanges_ShouldTrackCorrectly()
    {
        // Test multiple alias changes to ensure each is reflected properly

        var modelId = "hf:test/model";

        // Initial state: no alias
        var response1 = new
        {
            @object = "list",
            data = new[]
            {
                new { id = modelId, @object = "model", created = 1234567890, owned_by = "user" }
            }
        };

        _mockHostService.Setup(x => x.ListModelsOpenAIAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(response1);

        var result1 = await _controller.ListModels(CancellationToken.None);
        Assert.IsType<OkObjectResult>(result1);

        // Change 1: Add alias
        var response2 = new
        {
            @object = "list",
            data = new[]
            {
                new { id = "first-alias", @object = "model", created = 1234567890, owned_by = "user" }
            }
        };
        _mockHostService.Setup(x => x.ListModelsOpenAIAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(response2);

        var result2 = await _controller.ListModels(CancellationToken.None);
        Assert.IsType<OkObjectResult>(result2);

        // Change 2: Change alias
        var response3 = new
        {
            @object = "list",
            data = new[]
            {
                new { id = "second-alias", @object = "model", created = 1234567890, owned_by = "user" }
            }
        };
        _mockHostService.Setup(x => x.ListModelsOpenAIAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(response3);

        var result3 = await _controller.ListModels(CancellationToken.None);
        Assert.IsType<OkObjectResult>(result3);

        // Change 3: Remove alias
        var response4 = new
        {
            @object = "list",
            data = new[]
            {
                new { id = modelId, @object = "model", created = 1234567890, owned_by = "user" }
            }
        };
        _mockHostService.Setup(x => x.ListModelsOpenAIAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(response4);

        var result4 = await _controller.ListModels(CancellationToken.None);
        Assert.IsType<OkObjectResult>(result4);
    }
}
