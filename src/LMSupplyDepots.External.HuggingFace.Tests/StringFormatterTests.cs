using LMSupplyDepots.External.HuggingFace.Common;

namespace LMSupplyDepots.External.HuggingFace.Tests;

public class StringFormatterTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1048576, "1 MB")]
    [InlineData(1073741824, "1 GB")]
    public void FormatSize_ReturnsCorrectFormat(long bytes, string expected)
    {
        // Act
        var result = StringFormatter.FormatSize(bytes);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(-1)]
    public void FormatSize_NegativeValue_ThrowsArgumentException(long bytes)
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => StringFormatter.FormatSize(bytes));
    }

    [Fact]
    public void FormatTimeSpan_ValidTimeSpan_ReturnsFormattedString()
    {
        // Arrange
        var timeSpan = TimeSpan.FromHours(1.5);

        // Act
        var result = StringFormatter.FormatTimeSpan(timeSpan);

        // Assert
        Assert.Equal("01:30:00", result);
    }

    [Fact]
    public void FormatTimeSpan_NullTimeSpan_ReturnsUnknown()
    {
        // Act
        var result = StringFormatter.FormatTimeSpan(null);

        // Assert
        Assert.Equal("Unknown", result);
    }

    [Theory]
    [InlineData(0, "0%")]
    [InlineData(0.5, "50%")]
    [InlineData(1, "100%")]
    public void FormatProgress_ValidProgress_ReturnsFormattedString(double progress, string expected)
    {
        // Act
        var result = StringFormatter.FormatProgress(progress);

        // Assert
        Assert.Equal(expected, result);
    }
}
