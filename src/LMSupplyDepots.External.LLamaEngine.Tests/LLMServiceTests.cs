using LLama.Common;
using LMSupplyDepots.LLamaEngine.Models;
using LMSupplyDepots.LLamaEngine.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace LMSupplyDepots.LLamaEngine.Tests;

public class LLMServiceTests : IAsyncDisposable
{
    private readonly Mock<ILogger<LLMService>> _loggerMock;
    private readonly Mock<ILLamaModelManager> _modelManagerMock;
    private readonly Mock<ILLamaBackendService> _backendServiceMock;
    private readonly LLMService _service;
    private readonly string _testModelIdentifier = "test/model:file";

    public LLMServiceTests()
    {
        _loggerMock = new Mock<ILogger<LLMService>>();
        _modelManagerMock = new Mock<ILLamaModelManager>();
        _backendServiceMock = new Mock<ILLamaBackendService>();

        SetupDefaultMocks();

        _service = new LLMService(
            _loggerMock.Object,
            _modelManagerMock.Object,
            _backendServiceMock.Object);
    }

    private void SetupDefaultMocks()
    {
        var modelInfo = new LocalModelInfo
        {
            ModelId = _testModelIdentifier,
            FullPath = Path.Combine(AppContext.BaseDirectory, "test.gguf"),
            State = LocalModelState.Loaded
        };

        _modelManagerMock.Setup(x => x.NormalizeModelIdentifier(It.IsAny<string>()))
            .Returns((string id) => id);

        _modelManagerMock.Setup(x => x.GetModelInfoAsync(_testModelIdentifier))
            .ReturnsAsync(modelInfo);

        _backendServiceMock.Setup(x => x.GetOptimalModelParams(It.IsAny<string>()))
            .Returns(new ModelParams("test.gguf")
            {
                ContextSize = 2048,
                BatchSize = 512,
                Threads = 4,
                GpuLayerCount = 0
            });
    }

    [Fact]
    public async Task InferAsync_ValidatesModelState()
    {
        // Arrange
        var unloadedModel = new LocalModelInfo
        {
            ModelId = _testModelIdentifier,
            FullPath = "/path/to/model",
            State = LocalModelState.Unloaded
        };

        _modelManagerMock.Setup(x => x.GetModelInfoAsync(_testModelIdentifier))
            .ReturnsAsync(unloadedModel);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.InferAsync(_testModelIdentifier, "test prompt"));

        Assert.Contains("not loaded", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InferStreamAsync_ValidatesModelState()
    {
        // Arrange
        var unloadedModel = new LocalModelInfo
        {
            ModelId = _testModelIdentifier,
            FullPath = "/path/to/model",
            State = LocalModelState.Unloaded
        };

        _modelManagerMock.Setup(x => x.GetModelInfoAsync(_testModelIdentifier))
            .ReturnsAsync(unloadedModel);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in _service.InferStreamAsync(_testModelIdentifier, "test prompt"))
            {
                // Should not execute
            }
        });
    }

    [Fact]
    public async Task CreateEmbeddingAsync_ValidatesModelState()
    {
        // Arrange
        var unloadedModel = new LocalModelInfo
        {
            ModelId = _testModelIdentifier,
            FullPath = "/path/to/model",
            State = LocalModelState.Unloaded
        };

        _modelManagerMock.Setup(x => x.GetModelInfoAsync(_testModelIdentifier))
            .ReturnsAsync(unloadedModel);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateEmbeddingAsync(_testModelIdentifier, "test text"));
    }

    [Fact]
    public async Task InferAsync_UsesNormalizedModelIdentifier()
    {
        // Arrange
        var rawId = "test/model:file.gguf";
        var normalizedId = "test/model:file";

        _modelManagerMock.Setup(x => x.NormalizeModelIdentifier(rawId))
            .Returns(normalizedId);

        // Act
        try
        {
            await _service.InferAsync(rawId, "test prompt");
        }
        catch (Exception)
        {
            // Expected to fail due to mock setup
        }

        // Assert
        _modelManagerMock.Verify(x => x.GetModelInfoAsync(normalizedId), Times.Once);
    }

    public async ValueTask DisposeAsync()
    {
        await _service.DisposeAsync();
    }
}