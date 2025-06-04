using LLama;
using LLama.Common;
using LMSupplyDepots.LLamaEngine.Models;
using LMSupplyDepots.LLamaEngine.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace LMSupplyDepots.LLamaEngine.Tests;

public class LLamaModelManagerTests : IDisposable
{
    private readonly Mock<ILogger<LLamaModelManager>> _loggerMock;
    private readonly Mock<ILLamaBackendService> _backendServiceMock;
    private readonly LLamaModelManager _manager;
    private readonly string _testModelPath;
    private readonly string _testModelIdentifier = "provider/model:test";

    public LLamaModelManagerTests()
    {
        _loggerMock = new Mock<ILogger<LLamaModelManager>>();
        _backendServiceMock = new Mock<ILLamaBackendService>();
        _manager = new LLamaModelManager(_loggerMock.Object, _backendServiceMock.Object);
        _testModelPath = Path.Combine(Path.GetTempPath(), "test_model.gguf");

        // Setup default backend service behavior
        _backendServiceMock.Setup(x => x.GetOptimalModelParams(It.IsAny<string>()))
            .Returns((string path) => new ModelParams(path)
            {
                ContextSize = 2048,
                BatchSize = 512,
                Threads = 4,
                GpuLayerCount = 0
            });
    }

    [Fact]
    public void NormalizeModelIdentifier_RemovesGgufExtension()
    {
        // Arrange
        var modelId = "provider/model:test.gguf";

        // Act
        var normalized = _manager.NormalizeModelIdentifier(modelId);

        // Assert
        Assert.Equal("provider/model:test", normalized);
    }

    [Fact]
    public void NormalizeModelIdentifier_PreservesNonGgufIdentifier()
    {
        // Arrange
        var modelId = "provider/model:test";

        // Act
        var normalized = _manager.NormalizeModelIdentifier(modelId);

        // Assert
        Assert.Equal(modelId, normalized);
    }

    [Fact]
    public async Task GetModelInfoAsync_ReturnsNull_WhenModelNotFound()
    {
        // Act
        var result = await _manager.GetModelInfoAsync("nonexistent/model:test");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetLoadedModelsAsync_ReturnsEmptyList_WhenNoModelsLoaded()
    {
        // Act
        var models = await _manager.GetLoadedModelsAsync();

        // Assert
        Assert.Empty(models);
    }

    [Fact]
    public async Task LoadModelAsync_EmitsStateChangeEvents()
    {
        // Arrange
        var stateChanges = new List<ModelStateChangedEventArgs>();
        _manager.ModelStateChanged += (s, e) => stateChanges.Add(e);

        try
        {
            // Act
            await _manager.LoadModelAsync(_testModelPath, _testModelIdentifier);
        }
        catch (Exception)
        {
            // Expected to fail due to missing model file
        }

        // Assert
        Assert.Contains(stateChanges, e => e.NewState == LocalModelState.Loading);
        Assert.Contains(stateChanges, e => e.NewState == LocalModelState.Failed);
    }

    public void Dispose()
    {
        if (File.Exists(_testModelPath))
        {
            File.Delete(_testModelPath);
        }
    }
}