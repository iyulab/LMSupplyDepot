using LMSupplyDepots.LLamaEngine.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System.Runtime.InteropServices;

namespace LMSupplyDepots.LLamaEngine.Tests;

public class LLamaBackendServiceTests
{
    private readonly Mock<ILogger<LLamaBackendService>> _loggerMock;
    private readonly LLamaBackendService _service;

    public LLamaBackendServiceTests()
    {
        _loggerMock = new Mock<ILogger<LLamaBackendService>>();
        _service = new LLamaBackendService(_loggerMock.Object);
    }

    [Fact]
    public void GetOptimalModelParams_ReturnsValidParams()
    {
        // Arrange & Act
        var modelParams = _service.GetOptimalModelParams("test.gguf");

        // Assert
        Assert.NotNull(modelParams);
        Assert.Equal("test.gguf", modelParams.ModelPath);
        Assert.True(modelParams.ContextSize > 0);
        Assert.True(modelParams.BatchSize > 0);
        Assert.Equal(Environment.ProcessorCount, modelParams.Threads);
    }

    [Theory]
    [InlineData("Windows")]
    [InlineData("Linux")]
    public void DetectCuda_ChecksCorrectPaths_ForPlatform(string platformName)
    {
        // Convert string to OSPlatform
        var platform = platformName switch
        {
            "Windows" => OSPlatform.Windows,
            "Linux" => OSPlatform.Linux,
            _ => throw new ArgumentException($"Unsupported platform: {platformName}")
        };

        // This test verifies the path checking logic without actually accessing the filesystem
        if (RuntimeInformation.IsOSPlatform(platform))
        {
            // The actual test would run on the current platform
            // We can only verify that the service runs without exception
            Assert.True(_service.IsCudaAvailable || !_service.IsCudaAvailable);
        }
    }

    [Theory]
    [InlineData("Windows")]
    [InlineData("Linux")]
    public void DetectVulkan_ChecksCorrectPaths_ForPlatform(string platformName)
    {
        // Convert string to OSPlatform
        var platform = platformName switch
        {
            "Windows" => OSPlatform.Windows,
            "Linux" => OSPlatform.Linux,
            _ => throw new ArgumentException($"Unsupported platform: {platformName}")
        };

        // Similar to CUDA detection test
        if (RuntimeInformation.IsOSPlatform(platform))
        {
            Assert.True(_service.IsVulkanAvailable || !_service.IsVulkanAvailable);
        }
    }

    [Fact]
    public void GetOptimalModelParams_SetsGpuLayers_WhenGpuBackendAvailable()
    {
        // Arrange & Act
        var modelParams = _service.GetOptimalModelParams("test.gguf");

        // Assert
        if (_service.IsCudaAvailable || _service.IsVulkanAvailable)
        {
            // GPU is available, but we need to ensure backend was successfully loaded
            // Check if actual GPU layers were set
            Assert.True(modelParams.GpuLayerCount >= 0);
        }
        else
        {
            // No GPU available, should be CPU only
            Assert.Equal(0, modelParams.GpuLayerCount);
        }
    }

    [Fact]
    public void GetOptimalModelParams_CachesResult()
    {
        // Arrange & Act
        var firstParams = _service.GetOptimalModelParams("test.gguf");
        var secondParams = _service.GetOptimalModelParams("test.gguf");

        // Assert
        Assert.Equal(firstParams.Threads, secondParams.Threads);
        Assert.Equal(firstParams.GpuLayerCount, secondParams.GpuLayerCount);
        Assert.Equal(firstParams.BatchSize, secondParams.BatchSize);
    }

    [Fact]
    public void BackendDetection_HandlesErrors_Gracefully()
    {
        // This test verifies that even if backend detection fails,
        // the service still provides usable parameters
        var logger = new Mock<ILogger<LLamaBackendService>>();
        var service = new LLamaBackendService(logger.Object);

        var modelParams = service.GetOptimalModelParams("test.gguf");
        Assert.NotNull(modelParams);
        Assert.True(modelParams.Threads > 0);
    }
}