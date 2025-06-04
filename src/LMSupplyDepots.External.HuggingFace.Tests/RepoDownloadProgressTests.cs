using LMSupplyDepots.External.HuggingFace.Download;

namespace LMSupplyDepots.External.HuggingFace.Tests;

public class RepoDownloadProgressTests
{
    [Fact]
    public void Create_InitializesCorrectly()
    {
        // Arrange
        var files = new[] { "file1.txt", "file2.txt" };

        // Act
        var progress = RepoDownloadProgress.Create(files);

        // Assert
        Assert.False(progress.IsCompleted);
        Assert.Equal(2, progress.TotalFiles.Count);
        Assert.Empty(progress.CompletedFiles);
        Assert.Empty(progress.CurrentProgresses);
    }

    [Fact]
    public void WithProgress_UpdatesProgress()
    {
        // Arrange
        var files = new[] { "file1.txt", "file2.txt" };
        var progress = RepoDownloadProgress.Create(files);
        var completed = new[] { "file1.txt" };
        var current = new[]
        {
            FileDownloadProgress.CreateProgress("file2.txt", 50, 100, 10.0)
        };

        // Act
        var updated = progress.WithProgress(completed, current);

        // Assert
        Assert.Single(updated.CompletedFiles);
        Assert.Single(updated.CurrentProgresses);
        Assert.Equal(0.75, updated.TotalProgress); // One complete (0.5) + one half complete (0.25)
    }

    [Fact]
    public void AsCompleted_MarksAsComplete()
    {
        // Arrange
        var files = new[] { "file1.txt", "file2.txt" };
        var progress = RepoDownloadProgress.Create(files);

        // Act
        var completed = progress.AsCompleted();

        // Assert
        Assert.True(completed.IsCompleted);
        Assert.Equal(2, completed.CompletedFiles.Count);
        Assert.Empty(completed.CurrentProgresses);
        Assert.Equal(1.0, completed.TotalProgress);
    }
}