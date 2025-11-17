using LMSupplyDepots.External.HuggingFace.Client;
using LMSupplyDepots.ModelHub;
using LMSupplyDepots.ModelHub.HuggingFace;
using LMSupplyDepots.ModelHub.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace LMSupplyDepots.Host.Tests.HuggingFace;

/// <summary>
/// Integration tests for HuggingFaceDownloader with real API calls
/// These tests are marked as LocalIntegration and excluded from CICD
/// </summary>
public class HuggingFaceDownloaderIntegrationTests : IDisposable
{
    private readonly HuggingFaceDownloader _downloader;
    private readonly IHuggingFaceClient _client;
    private readonly string _testOutputDirectory;

    public HuggingFaceDownloaderIntegrationTests()
    {
        var downloaderOptions = Options.Create(new HuggingFaceDownloaderOptions
        {
            ApiToken = Environment.GetEnvironmentVariable("HUGGINGFACE_API_TOKEN"), // null if not set (public models only)
            MaxConcurrentFileDownloads = 3,
            RequestTimeout = TimeSpan.FromMinutes(10),
            MaxRetries = 3
        });

        var hubOptions = Options.Create(new ModelHubOptions
        {
            ModelsDirectory = Path.Combine(Path.GetTempPath(), "LMSupplyDepots_IntegrationTests", Guid.NewGuid().ToString())
        });

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        var downloaderLogger = loggerFactory.CreateLogger<HuggingFaceDownloader>();

        // Create HuggingFaceClient
        var clientOptions = new HuggingFaceClientOptions
        {
            Token = downloaderOptions.Value.ApiToken,
            MaxConcurrentDownloads = downloaderOptions.Value.MaxConcurrentFileDownloads,
            Timeout = downloaderOptions.Value.RequestTimeout,
            MaxRetries = downloaderOptions.Value.MaxRetries
        };
        _client = new HuggingFaceClient(clientOptions, loggerFactory);

        // Create file system repository
        var repositoryLogger = loggerFactory.CreateLogger<FileSystemModelRepository>();
        var repository = new FileSystemModelRepository(hubOptions, repositoryLogger);

        // Create downloader with DI
        _downloader = new HuggingFaceDownloader(downloaderOptions, hubOptions, downloaderLogger, _client, repository);

        _testOutputDirectory = Path.Combine(Path.GetTempPath(), "LMSupplyDepots_IntegrationTests", "downloads", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testOutputDirectory);
    }

    /// <summary>
    /// Integration test: Download small GGUF model with artifact name WITHOUT extension
    /// Tests backward compatibility (existing behavior)
    /// </summary>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    [Trait("Priority", "High")]
    public async Task DownloadModel_WithoutExtension_DownloadsSuccessfully()
    {
        // Arrange - Use a small test model (few MB)
        var sourceId = "hf:TheBloke/TinyLlama-1.1B-Chat-v1.0-GGUF/tinyllama-1.1b-chat-v1.0.Q2_K";
        var targetDirectory = Path.Combine(_testOutputDirectory, "test_without_extension");

        try
        {
            // Act
            var result = await _downloader.DownloadModelAsync(sourceId, targetDirectory, progress: null, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.True(Directory.Exists(targetDirectory), "Target directory should exist");

            var downloadedFiles = Directory.GetFiles(targetDirectory, "*.gguf");
            Assert.NotEmpty(downloadedFiles);

            var mainFile = downloadedFiles.FirstOrDefault();
            Assert.NotNull(mainFile);

            // Verify no double extension
            Assert.DoesNotContain(".gguf.gguf", mainFile);
            Assert.EndsWith(".gguf", mainFile, StringComparison.OrdinalIgnoreCase);

            // Verify file size (should be > 0)
            var fileInfo = new FileInfo(mainFile);
            Assert.True(fileInfo.Length > 0, "Downloaded file should have content");
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(targetDirectory))
            {
                Directory.Delete(targetDirectory, true);
            }
        }
    }

    /// <summary>
    /// Integration test: Download model with artifact name INCLUDING .gguf extension
    /// Tests the bug fix - ensures no double extension
    /// </summary>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    [Trait("Priority", "Critical")]
    public async Task DownloadModel_WithExtension_NoDoubleExtension()
    {
        // Arrange - Use a small test model with .gguf extension in artifact name
        var sourceId = "hf:TheBloke/TinyLlama-1.1B-Chat-v1.0-GGUF/tinyllama-1.1b-chat-v1.0.Q2_K.gguf";
        var targetDirectory = Path.Combine(_testOutputDirectory, "test_with_extension");

        try
        {
            // Act
            var result = await _downloader.DownloadModelAsync(sourceId, targetDirectory, progress: null, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.True(Directory.Exists(targetDirectory), "Target directory should exist");

            var downloadedFiles = Directory.GetFiles(targetDirectory, "*.gguf");
            Assert.NotEmpty(downloadedFiles);

            var mainFile = downloadedFiles.FirstOrDefault();
            Assert.NotNull(mainFile);

            // CRITICAL: Verify no double extension (the bug fix validation)
            Assert.DoesNotContain(".gguf.gguf", mainFile);
            Assert.EndsWith(".gguf", mainFile, StringComparison.OrdinalIgnoreCase);

            // Count .gguf occurrences (should be exactly 1)
            var fileName = Path.GetFileName(mainFile);
            var ggufCount = fileName.Split(new[] { ".gguf" }, StringSplitOptions.None).Length - 1;
            Assert.Equal(1, ggufCount);

            // Verify file size
            var fileInfo = new FileInfo(mainFile);
            Assert.True(fileInfo.Length > 0, "Downloaded file should have content");
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(targetDirectory))
            {
                Directory.Delete(targetDirectory, true);
            }
        }
    }

    /// <summary>
    /// Integration test: Verify both extension formats produce identical results
    /// </summary>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    [Trait("Priority", "High")]
    public async Task DownloadModel_BothFormats_ProduceIdenticalResults()
    {
        // Arrange
        var artifactNameWithoutExt = "tinyllama-1.1b-chat-v1.0.Q2_K";
        var artifactNameWithExt = "tinyllama-1.1b-chat-v1.0.Q2_K.gguf";

        var sourceIdWithoutExt = $"hf:TheBloke/TinyLlama-1.1B-Chat-v1.0-GGUF/{artifactNameWithoutExt}";
        var sourceIdWithExt = $"hf:TheBloke/TinyLlama-1.1B-Chat-v1.0-GGUF/{artifactNameWithExt}";

        var targetDir1 = Path.Combine(_testOutputDirectory, "format_test_1");
        var targetDir2 = Path.Combine(_testOutputDirectory, "format_test_2");

        try
        {
            // Act
            var result1 = await _downloader.DownloadModelAsync(sourceIdWithoutExt, targetDir1, progress: null, CancellationToken.None);
            var result2 = await _downloader.DownloadModelAsync(sourceIdWithExt, targetDir2, progress: null, CancellationToken.None);

            // Assert
            Assert.NotNull(result1);
            Assert.NotNull(result2);

            var files1 = Directory.GetFiles(targetDir1, "*.gguf");
            var files2 = Directory.GetFiles(targetDir2, "*.gguf");

            Assert.Single(files1);
            Assert.Single(files2);

            var fileName1 = Path.GetFileName(files1[0]);
            var fileName2 = Path.GetFileName(files2[0]);

            // Both should produce exactly the same filename
            Assert.Equal(fileName1, fileName2, StringComparer.OrdinalIgnoreCase);

            // Both should have exactly one .gguf extension
            Assert.EndsWith(".gguf", fileName1, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(".gguf.gguf", fileName1);
            Assert.EndsWith(".gguf", fileName2, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(".gguf.gguf", fileName2);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(targetDir1)) Directory.Delete(targetDir1, true);
            if (Directory.Exists(targetDir2)) Directory.Delete(targetDir2, true);
        }
    }

    /// <summary>
    /// Integration test: Verify case-insensitive extension handling
    /// </summary>
    [Theory]
    [InlineData("tinyllama-1.1b-chat-v1.0.Q2_K.gguf")]
    [InlineData("tinyllama-1.1b-chat-v1.0.Q2_K.GGUF")]
    [InlineData("tinyllama-1.1b-chat-v1.0.Q2_K.Gguf")]
    [Trait("Category", "LocalIntegration")]
    [Trait("Priority", "Medium")]
    public async Task DownloadModel_CaseInsensitiveExtension_HandlesCorrectly(string artifactName)
    {
        // Arrange
        var sourceId = $"hf:TheBloke/TinyLlama-1.1B-Chat-v1.0-GGUF/{artifactName}";
        var targetDirectory = Path.Combine(_testOutputDirectory, $"case_test_{Guid.NewGuid()}");

        try
        {
            // Act
            var result = await _downloader.DownloadModelAsync(sourceId, targetDirectory, progress: null, CancellationToken.None);

            // Assert
            Assert.NotNull(result);

            var downloadedFiles = Directory.GetFiles(targetDirectory, "*.gguf");
            Assert.NotEmpty(downloadedFiles);

            var mainFile = downloadedFiles.FirstOrDefault();
            Assert.NotNull(mainFile);

            // Verify no double extension regardless of case
            var fileName = Path.GetFileName(mainFile).ToLowerInvariant();
            var ggufOccurrences = fileName.Split(new[] { ".gguf" }, StringSplitOptions.None).Length - 1;
            Assert.Equal(1, ggufOccurrences);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(targetDirectory))
            {
                Directory.Delete(targetDirectory, true);
            }
        }
    }

    /// <summary>
    /// Cleanup test resources
    /// </summary>
    public void Dispose()
    {
        try
        {
            _client?.Dispose();
            _downloader?.Dispose();

            if (Directory.Exists(_testOutputDirectory))
            {
                Directory.Delete(_testOutputDirectory, true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }
}
