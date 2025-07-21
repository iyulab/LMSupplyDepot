using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using LMSupplyDepots.Host.Controllers;
using LMSupplyDepots.Host;
using LMSupplyDepots.Models;
using LMSupplyDepots.Host.Models.OpenAI;
using Xunit;

namespace LMSupplyDepots.Host.Tests.Controllers;

/// <summary>
/// Scenario-based tests for V1Controller alias functionality
/// </summary>
public class V1ControllerAliasScenarioTests
{
    private readonly Mock<IHostService> _mockHostService;
    private readonly Mock<ILogger<V1Controller>> _mockLogger;
    private readonly V1Controller _controller;

    public V1ControllerAliasScenarioTests()
    {
        _mockHostService = new Mock<IHostService>();
        _mockLogger = new Mock<ILogger<V1Controller>>();
        _controller = new V1Controller(_mockHostService.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task Scenario_HyperCLOVAXWithAlias_ReturnsAliasAsModelId()
    {
        // Scenario: User has loaded HyperCLOVA X model and set alias "hyperclovax"
        // Expected: /v1/models should return "hyperclovax" as the model ID instead of the full model ID

        // Arrange
        var modelWithAlias = new LMModel
        {
            Id = "hf:DevQuasar/naver-hyperclovax.HyperCLOVAX-SEED-Text-Instruct-0.5B-GGUF/naver-hyperclovax.HyperCLOVAX-SEED-Text-Instruct-0.5B.f16",
            Alias = "hyperclovax",
            Name = "naver-hyperclovax.HyperCLOVAX-SEED-Text-Instruct-0.5B-GGUF",
            Description = "DevQuasar/naver-hyperclovax.HyperCLOVAX-SEED-Text-Instruct-0.5B-GGUF - Tags: gguf, text-generation",
            Type = ModelType.TextGeneration,
            IsLoaded = true
        };

        var loadedModels = new List<LMModel> { modelWithAlias };

        _mockHostService
            .Setup(x => x.GetLoadedModelsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(loadedModels);

        // Act
        var result = await _controller.ListModels(CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<OpenAIModelsResponse>(okResult.Value);

        Assert.Single(response.Data);

        var returnedModel = response.Data.First();
        Assert.Equal("hyperclovax", returnedModel.Id); // This should be the alias, not the full ID
        Assert.Equal("local", returnedModel.OwnedBy);
        Assert.Equal("text-generation", returnedModel.Type);
        Assert.True(returnedModel.Created > 0);
    }

    [Fact]
    public async Task Scenario_MultipleModels_MixedAliasUsage_ReturnsCorrectIds()
    {
        // Scenario: User has multiple models loaded, some with aliases, some without
        // Expected: Models with aliases should show alias as ID, others should show full ID

        // Arrange
        var models = new List<LMModel>
        {
            new LMModel
            {
                Id = "hf:DevQuasar/naver-hyperclovax.HyperCLOVAX-SEED-Text-Instruct-0.5B-GGUF/naver-hyperclovax.HyperCLOVAX-SEED-Text-Instruct-0.5B.f16",
                Alias = "hyperclovax",
                Name = "HyperCLOVA X",
                Type = ModelType.TextGeneration,
                IsLoaded = true
            },
            new LMModel
            {
                Id = "hf:microsoft/DialoGPT-medium",
                Alias = "", // Empty alias should be treated as no alias
                Name = "DialoGPT Medium",
                Type = ModelType.TextGeneration,
                IsLoaded = true
            },
            new LMModel
            {
                Id = "hf:sentence-transformers/all-MiniLM-L6-v2",
                Alias = "embeddings-model",
                Name = "All MiniLM L6 v2",
                Type = ModelType.Embedding,
                IsLoaded = true
            },
            new LMModel
            {
                Id = "local:my-custom-model",
                Alias = null, // Null alias should be treated as no alias
                Name = "My Custom Model",
                Type = ModelType.TextGeneration,
                IsLoaded = true
            }
        };

        _mockHostService
            .Setup(x => x.GetLoadedModelsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(models);

        // Act
        var result = await _controller.ListModels(CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<OpenAIModelsResponse>(okResult.Value);

        Assert.Equal(4, response.Data.Count);

        // Find models by expected ID (should be alias when available, full ID otherwise)
        var hyperCLOVAX = response.Data.FirstOrDefault(m => m.Id == "hyperclovax");
        var dialoGPT = response.Data.FirstOrDefault(m => m.Id == "hf:microsoft/DialoGPT-medium");
        var embeddingModel = response.Data.FirstOrDefault(m => m.Id == "embeddings-model");
        var customModel = response.Data.FirstOrDefault(m => m.Id == "local:my-custom-model");

        // Verify models with aliases use alias as ID
        Assert.NotNull(hyperCLOVAX);
        Assert.Equal("hyperclovax", hyperCLOVAX.Id);
        Assert.Equal("text-generation", hyperCLOVAX.Type);

        Assert.NotNull(embeddingModel);
        Assert.Equal("embeddings-model", embeddingModel.Id);
        Assert.Equal("embedding", embeddingModel.Type);

        // Verify models without aliases use full ID
        Assert.NotNull(dialoGPT);
        Assert.Equal("hf:microsoft/DialoGPT-medium", dialoGPT.Id);
        Assert.Equal("text-generation", dialoGPT.Type);

        Assert.NotNull(customModel);
        Assert.Equal("local:my-custom-model", customModel.Id);
        Assert.Equal("text-generation", customModel.Type);
    }

    [Fact]
    public async Task Scenario_AliasChangedDuringRuntime_ReturnsNewAlias()
    {
        // Scenario: User changes an alias for a loaded model
        // Expected: Next call to /v1/models should return the new alias as ID

        // Arrange - Initial state with old alias
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

        // Act - First call
        var result1 = await _controller.ListModels(CancellationToken.None);

        // Assert - Should return old alias
        var okResult1 = Assert.IsType<OkObjectResult>(result1.Result);
        var response1 = Assert.IsType<OpenAIModelsResponse>(okResult1.Value);
        Assert.Equal("old-alias", response1.Data.First().Id);

        // Arrange - Change alias
        var modelWithNewAlias = new LMModel
        {
            Id = "hf:DevQuasar/naver-hyperclovax.HyperCLOVAX-SEED-Text-Instruct-0.5B-GGUF/naver-hyperclovax.HyperCLOVAX-SEED-Text-Instruct-0.5B.f16",
            Alias = "hyperclovax", // New alias
            Name = "HyperCLOVA X",
            Type = ModelType.TextGeneration,
            IsLoaded = true
        };

        _mockHostService
            .Setup(x => x.GetLoadedModelsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LMModel> { modelWithNewAlias });

        // Act - Second call
        var result2 = await _controller.ListModels(CancellationToken.None);

        // Assert - Should return new alias
        var okResult2 = Assert.IsType<OkObjectResult>(result2.Result);
        var response2 = Assert.IsType<OpenAIModelsResponse>(okResult2.Value);
        Assert.Equal("hyperclovax", response2.Data.First().Id);
    }

    [Fact]
    public async Task Scenario_AliasRemoved_ReturnsFullId()
    {
        // Scenario: User removes an alias from a model
        // Expected: /v1/models should return the full model ID instead of alias

        // Arrange - Model with no alias (alias removed)
        var modelWithoutAlias = new LMModel
        {
            Id = "hf:DevQuasar/naver-hyperclovax.HyperCLOVAX-SEED-Text-Instruct-0.5B-GGUF/naver-hyperclovax.HyperCLOVAX-SEED-Text-Instruct-0.5B.f16",
            Alias = null, // Alias removed
            Name = "HyperCLOVA X",
            Type = ModelType.TextGeneration,
            IsLoaded = true
        };

        _mockHostService
            .Setup(x => x.GetLoadedModelsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LMModel> { modelWithoutAlias });

        // Act
        var result = await _controller.ListModels(CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<OpenAIModelsResponse>(okResult.Value);

        Assert.Single(response.Data);
        var returnedModel = response.Data.First();
        Assert.Equal("hf:DevQuasar/naver-hyperclovax.HyperCLOVAX-SEED-Text-Instruct-0.5B-GGUF/naver-hyperclovax.HyperCLOVAX-SEED-Text-Instruct-0.5B.f16", returnedModel.Id);
    }
}
