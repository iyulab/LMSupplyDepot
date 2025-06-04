using LMSupplyDepots.External.HuggingFace.Download;

namespace LMSupplyDepots.External.HuggingFace.Tests;

public class FileDownloadProgressTests
{
    [Fact]
    public void CreateCompleted_ReturnsCompletedProgress()
    {
        // Arrange
        var path = "test.file";
        var size = 1000L;

        // Act
        var progress = FileDownloadProgress.CreateCompleted(path, size);

        // Assert
        Assert.True(progress.IsCompleted);
        Assert.Equal(path, progress.UploadPath);
        Assert.Equal(size, progress.CurrentBytes);
        Assert.Equal(size, progress.TotalBytes);
        Assert.Equal(1.0, progress.DownloadProgress);
    }

    [Fact]
    public void CreateProgress_ReturnsProgressInfo()
    {
        // Arrange
        var path = "test.file";
        var current = 500L;
        var total = 1000L;
        var speed = 100.0;

        // Act
        var progress = FileDownloadProgress.CreateProgress(path, current, total, speed);

        // Assert
        Assert.False(progress.IsCompleted);
        Assert.Equal(path, progress.UploadPath);
        Assert.Equal(current, progress.CurrentBytes);
        Assert.Equal(total, progress.TotalBytes);
        Assert.Equal(0.5, progress.DownloadProgress);
    }
}
