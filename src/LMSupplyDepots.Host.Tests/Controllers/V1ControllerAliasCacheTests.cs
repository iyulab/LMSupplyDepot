using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using LMSupplyDepots.Host.Controllers;
using LMSupplyDepots.Host;
using LMSupplyDepots.Models;
using LMSupplyDepots.Host.Models.OpenAI;
using Xunit;
using LMSupplyDepots.Host.Services;

namespace LMSupplyDepots.Host.Tests.Controllers;

/// <summary>
/// Tests for the specific issue where alias changes are not reflected in /v1/models
/// </summary>
public class V1ControllerAliasCacheTests
{
    private readonly Mock<IHostService> _mockHostService;
    private readonly Mock<IOpenAIConverterService> _mockConverter;
    private readonly Mock<ILogger<V1Controller>> _mockLogger;
    private readonly V1Controller _controller;

    public V1ControllerAliasCacheTests()
    {
        _mockHostService = new Mock<IHostService>();
        _mockConverter = new Mock<IOpenAIConverterService>();
        _mockLogger = new Mock<ILogger<V1Controller>>();
        _controller = new V1Controller(_mockHostService.Object, _mockConverter.Object, _mockLogger.Object);
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
        var modelWithOldAlias = new LMModel
        {
            Id = "hf:DevQuasar/naver-hyperclovax.HyperCLOVAX-SEED-Text-Instruct-0.5B-GGUF/naver-hyperclovax.HyperCLOVAX-SEED-Text-Instruct-0.5B.f16",
            Alias = "old-alias",
            Name = "HyperCLOVA X",
            Type = ModelType.TextGeneration,
            IsLoaded = true
        };

        _mockHostService
            .Setup(x => x.GetLoadedModelsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LMModel> { modelWithOldAlias });

        // Step 3: First call to /v1/models should show old-alias
        var result1 = await _controller.ListModels(CancellationToken.None);
        var okResult1 = Assert.IsType<OkObjectResult>(result1.Result);
        var response1 = Assert.IsType<OpenAIModelsResponse>(okResult1.Value);

        Assert.Single(response1.Data);
        Assert.Equal("old-alias", response1.Data.First().Id);

        // Step 4: Alias is changed (simulated by updating the mock to return updated model)
        var modelWithNewAlias = new LMModel
        {
            Id = "hf:DevQuasar/naver-hyperclovax.HyperCLOVAX-SEED-Text-Instruct-0.5B-GGUF/naver-hyperclovax.HyperCLOVAX-SEED-Text-Instruct-0.5B.f16",
            Alias = "new-alias", // Changed from old-alias to new-alias
            Name = "HyperCLOVA X",
            Type = ModelType.TextGeneration,
            IsLoaded = true
        };

        _mockHostService
            .Setup(x => x.GetLoadedModelsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LMModel> { modelWithNewAlias });

        // Step 5: Second call to /v1/models should now show new-alias (THIS WAS THE BUG)
        var result2 = await _controller.ListModels(CancellationToken.None);
        var okResult2 = Assert.IsType<OkObjectResult>(result2.Result);
        var response2 = Assert.IsType<OpenAIModelsResponse>(okResult2.Value);

        Assert.Single(response2.Data);
        Assert.Equal("new-alias", response2.Data.First().Id); // This should pass after the fix
    }

    [Fact]
    public async Task Scenario_AliasRemovedAfterLoad_ShouldShowFullId()
    {
        // Test the scenario where alias is completely removed

        // Step 1: Model loaded with alias
        var modelWithAlias = new LMModel
        {
            Id = "hf:test/model",
            Alias = "test-alias",
            Name = "Test Model",
            Type = ModelType.TextGeneration,
            IsLoaded = true
        };

        _mockHostService
            .Setup(x => x.GetLoadedModelsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LMModel> { modelWithAlias });

        var result1 = await _controller.ListModels(CancellationToken.None);
        var okResult1 = Assert.IsType<OkObjectResult>(result1.Result);
        var response1 = Assert.IsType<OpenAIModelsResponse>(okResult1.Value);

        Assert.Equal("test-alias", response1.Data.First().Id);

        // Step 2: Alias removed
        var modelWithoutAlias = new LMModel
        {
            Id = "hf:test/model",
            Alias = null, // Alias removed
            Name = "Test Model",
            Type = ModelType.TextGeneration,
            IsLoaded = true
        };

        _mockHostService
            .Setup(x => x.GetLoadedModelsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LMModel> { modelWithoutAlias });

        var result2 = await _controller.ListModels(CancellationToken.None);
        var okResult2 = Assert.IsType<OkObjectResult>(result2.Result);
        var response2 = Assert.IsType<OpenAIModelsResponse>(okResult2.Value);

        Assert.Equal("hf:test/model", response2.Data.First().Id); // Should show full ID now
    }

    [Fact]
    public async Task Scenario_MultipleAliasChanges_ShouldTrackCorrectly()
    {
        // Test multiple alias changes to ensure each is reflected properly

        var modelId = "hf:test/model";

        // Initial state: no alias
        var model1 = new LMModel
        {
            Id = modelId,
            Alias = null,
            Name = "Test Model",
            Type = ModelType.TextGeneration,
            IsLoaded = true
        };

        _mockHostService.Setup(x => x.GetLoadedModelsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LMModel> { model1 });

        var result1 = await _controller.ListModels(CancellationToken.None);
        Assert.Equal(modelId, ((OpenAIModelsResponse)((OkObjectResult)result1.Result!).Value!).Data.First().Id);

        // Change 1: Add alias
        var model2 = new LMModel
        {
            Id = modelId,
            Alias = "first-alias",
            Name = "Test Model",
            Type = ModelType.TextGeneration,
            IsLoaded = true
        };
        _mockHostService.Setup(x => x.GetLoadedModelsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LMModel> { model2 });

        var result2 = await _controller.ListModels(CancellationToken.None);
        Assert.Equal("first-alias", ((OpenAIModelsResponse)((OkObjectResult)result2.Result!).Value!).Data.First().Id);

        // Change 2: Change alias
        var model3 = new LMModel
        {
            Id = modelId,
            Alias = "second-alias",
            Name = "Test Model",
            Type = ModelType.TextGeneration,
            IsLoaded = true
        };
        _mockHostService.Setup(x => x.GetLoadedModelsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LMModel> { model3 });

        var result3 = await _controller.ListModels(CancellationToken.None);
        Assert.Equal("second-alias", ((OpenAIModelsResponse)((OkObjectResult)result3.Result!).Value!).Data.First().Id);

        // Change 3: Remove alias
        var model4 = new LMModel
        {
            Id = modelId,
            Alias = null,
            Name = "Test Model",
            Type = ModelType.TextGeneration,
            IsLoaded = true
        };
        _mockHostService.Setup(x => x.GetLoadedModelsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LMModel> { model4 });

        var result4 = await _controller.ListModels(CancellationToken.None);
        Assert.Equal(modelId, ((OpenAIModelsResponse)((OkObjectResult)result4.Result!).Value!).Data.First().Id);
    }
}
