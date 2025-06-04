using LMSupplyDepots.External.HuggingFace.Client;

namespace LMSupplyDepots.External.HuggingFace.Tests;

public class HuggingFaceClientOptionsTests
{
    [Fact]
    public void Constructor_DefaultValues()
    {
        // Act
        var options = new HuggingFaceClientOptions();

        // Assert
        Assert.Null(options.Token);
        Assert.Equal(5, options.MaxConcurrentDownloads);
        Assert.Equal(100, options.ProgressUpdateInterval);
        Assert.Equal(TimeSpan.FromMinutes(5), options.Timeout);
        Assert.Equal(8192, options.BufferSize);
        Assert.Equal(1, options.MaxRetries);
        Assert.Equal(1000, options.RetryDelayMilliseconds);
    }

    [Fact]
    public void Validate_ValidOptions_NoException()
    {
        // Arrange
        var options = new HuggingFaceClientOptions
        {
            Token = "test_token"
        };

        // Act & Assert
        options.Validate();
    }

    [Theory]
    [InlineData(0)]  // Too low
    [InlineData(21)] // Too high
    public void MaxConcurrentDownloads_InvalidValue_ThrowsArgumentException(int value)
    {
        // Arrange
        var options = new HuggingFaceClientOptions();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => options.MaxConcurrentDownloads = value);
    }

    [Fact]
    public void Reset_ResetsAllValues()
    {
        // Arrange
        var options = new HuggingFaceClientOptions
        {
            Token = "test_token",
            MaxConcurrentDownloads = 10,
            ProgressUpdateInterval = 200,
            MaxRetries = 2
        };

        // Act
        options.Reset();

        // Assert
        Assert.Null(options.Token);
        Assert.Equal(5, options.MaxConcurrentDownloads);
        Assert.Equal(100, options.ProgressUpdateInterval);
        Assert.Equal(1, options.MaxRetries);
    }
}
