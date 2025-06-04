using LMSupplyDepots.External.HuggingFace.Client;
using LMSupplyDepots.External.HuggingFace.Common;
using LMSupplyDepots.External.HuggingFace.Download;
using LMSupplyDepots.External.HuggingFace.Models;
using LMSupplyDepots.External.HuggingFace.Tests.Core;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net;

namespace LMSupplyDepots.External.HuggingFace.Tests;

public class HuggingFaceClientTests
{
    private readonly Mock<ILogger<HuggingFaceClient>> _loggerMock;
    private readonly Mock<ILoggerFactory> _loggerFactoryMock;
    private readonly HuggingFaceClientOptions _options;
    private readonly MockHttpMessageHandler _mockHandler;
    private readonly HuggingFaceClient _client;

    public HuggingFaceClientTests()
    {
        _loggerMock = new Mock<ILogger<HuggingFaceClient>>();
        _loggerFactoryMock = new Mock<ILoggerFactory>();
        _loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>()))
            .Returns(_loggerMock.Object);

        _options = new HuggingFaceClientOptions
        {
            Token = "test_token",
            MaxRetries = 1
        };

        _mockHandler = new MockHttpMessageHandler();
        _client = new HuggingFaceClient(_options, _mockHandler, _loggerFactoryMock.Object);
    }

    [Fact]
    public async Task SearchTextGenerationModelsAsync_WithValidParameters_ReturnsGgufModels()
    {
        // Act
        var result = await _client.SearchTextGenerationModelsAsync(
            search: "test",
            filters: ["test-filter"],
            limit: 2);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);  // text-generation 태그가 있는 모델만 반환
        foreach (var model in result)
        {
            Assert.True(model.HasGgufFiles());
            Assert.True(ModelTagValidation.IsTextGenerationModel(model));
        }
    }

    [Fact]
    public async Task SearchEmbeddingModelsAsync_WithValidParameters_ReturnsGgufModels()
    {
        // Act
        var result = await _client.SearchEmbeddingModelsAsync(
            search: "test",
            filters: ["test-filter"],
            limit: 2);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        foreach (var model in result)
        {
            Assert.True(model.HasGgufFiles());
            Assert.True(ModelTagValidation.IsEmbeddingModel(model));
            Assert.Contains("sentence-similarity", model.Tags);
        }
    }

    [Fact]
    public async Task FindModelByRepoIdAsync_WithValidId_ReturnsModel()
    {
        // Arrange
        var repoId = "test/model";

        // Act
        var result = await _client.FindModelByRepoIdAsync(repoId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(repoId, result.Id);
        var filePaths = result.GetFilePaths();
        Assert.NotEmpty(filePaths);
    }

    [Fact]
    public async Task FindModelByRepoIdAsync_WithInvalidId_ThrowsException()
    {
        // Arrange
        var repoId = "nonexistent/model";

        // Act & Assert
        var exception = await Assert.ThrowsAsync<HuggingFaceException>(
            () => _client.FindModelByRepoIdAsync(repoId));
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task GetFileInfoAsync_WithValidPath_ReturnsFileInfo()
    {
        // Arrange
        var repoId = "test/model";
        var filePath = "config.json";

        // Act
        var result = await _client.GetFileInfoAsync(repoId, filePath);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("config.json", result.Name);
        Assert.Equal(filePath, result.Path);
        Assert.NotNull(result.Size);
        Assert.NotNull(result.MimeType);
    }

    [Fact]
    public async Task GetFileInfoAsync_WithInvalidPath_ThrowsException()
    {
        // Arrange
        var repoId = "test/model";
        var filePath = "nonexistent.txt";

        // Act & Assert
        await Assert.ThrowsAsync<HuggingFaceException>(
            () => _client.GetFileInfoAsync(repoId, filePath));
    }

    [Fact]
    public async Task DownloadFileWithResultAsync_WithValidFile_DownloadsSuccessfully()
    {
        // Arrange
        var repoId = "test/model";
        var filePath = "test.bin";
        var outputPath = Path.GetTempFileName();
        var progress = new Progress<FileDownloadProgress>();

        try
        {
            // Act
            var result = await _client.DownloadFileWithResultAsync(
                repoId,
                filePath,
                outputPath,
                progress: progress);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.IsCompleted);
            Assert.Equal(outputPath, result.FilePath);
            Assert.True(result.BytesDownloaded > 0);
            Assert.True(File.Exists(outputPath));
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task DownloadRepositoryAsync_WithValidRepo_DownloadsFiles()
    {
        // Arrange
        var repoId = "test/model";
        var outputDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(outputDir);

        try
        {
            // Act
            await foreach (var progress in _client.DownloadRepositoryAsync(repoId, outputDir))
            {
                // Assert progress
                Assert.NotNull(progress);
                Assert.NotEmpty(progress.TotalFiles);
                Assert.True(progress.TotalProgress >= 0 && progress.TotalProgress <= 1);
            }

            // Assert directory is not empty
            Assert.True(Directory.GetFiles(outputDir, "*", SearchOption.AllDirectories).Length > 0);
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, true);
        }
    }

    [Fact]
    public void GetDownloadUrl_WithValidParameters_ReturnsUrl()
    {
        // Arrange
        var repoId = "test/model";
        var filePath = "test.bin";

        // Act
        var url = _client.GetDownloadUrl(repoId, filePath);

        // Assert
        Assert.NotNull(url);
        Assert.Contains(repoId, url);
        Assert.Contains(Uri.EscapeDataString(filePath), url);
        Assert.StartsWith("https://", url);
    }

    [Fact]
    public void Dispose_CallsDispose_OnlyOnce()
    {
        // Act
        _client.Dispose();
        _client.Dispose(); // Second call should not throw

        // Assert - no exception thrown
    }
}