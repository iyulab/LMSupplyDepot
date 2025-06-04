using LMSupplyDepots.External.HuggingFace.Common;
using System.Collections.Immutable;

namespace LMSupplyDepots.External.HuggingFace.Download;

/// <summary>
/// Represents immutable information about the progress of a repository download.
/// </summary>
public record RepoDownloadProgress
{
    /// <summary>
    /// Gets a value indicating whether the download is completed.
    /// </summary>
    public bool IsCompleted { get; init; }

    /// <summary>
    /// Gets the total file paths to download.
    /// </summary>
    public IImmutableSet<string> TotalFiles { get; init; } = ImmutableHashSet<string>.Empty;

    /// <summary>
    /// Gets the file paths that have been completed.
    /// </summary>
    public IImmutableSet<string> CompletedFiles { get; init; } = ImmutableHashSet<string>.Empty;

    /// <summary>
    /// Gets the current download progresses.
    /// </summary>
    public IImmutableList<FileDownloadProgress> CurrentProgresses { get; init; } = ImmutableList<FileDownloadProgress>.Empty;

    /// <summary>
    /// Gets the file paths that have not been downloaded.
    /// </summary>
    public IImmutableSet<string> RemainingFiles =>
        TotalFiles.Except(CompletedFiles).ToImmutableHashSet();

    /// <summary>
    /// Gets the total progress of the download (0.0 to 1.0).
    /// </summary>
    public double TotalProgress
    {
        get
        {
            if (!TotalFiles.Any())
                return 0.0;

            var totalFiles = TotalFiles.Count;
            var completedProgress = (double)CompletedFiles.Count / totalFiles;
            var currentProgress = CurrentProgresses.Sum(p => p.DownloadProgress ?? 0.0) / totalFiles;

            return completedProgress + currentProgress;
        }
    }

    /// <summary>
    /// Creates a new instance of RepoDownloadProgress with the specified files.
    /// </summary>
    public static RepoDownloadProgress Create(IEnumerable<string> files) =>
        new()
        {
            IsCompleted = false,
            TotalFiles = files.ToImmutableHashSet()
        };

    /// <summary>
    /// Creates a new instance with updated progress information.
    /// </summary>
    public RepoDownloadProgress WithProgress(
        IEnumerable<string> completedFiles,
        IEnumerable<FileDownloadProgress> currentProgresses) =>
        this with
        {
            CompletedFiles = completedFiles.ToImmutableHashSet(),
            CurrentProgresses = currentProgresses.ToImmutableList()
        };

    /// <summary>
    /// Creates a completed instance.
    /// </summary>
    public RepoDownloadProgress AsCompleted() =>
        this with
        {
            IsCompleted = true,
            CompletedFiles = TotalFiles,
            CurrentProgresses = ImmutableList<FileDownloadProgress>.Empty
        };

    public override string ToString()
    {
        var currentProgressDetails = string.Join("\n",
            CurrentProgresses.Select(p => $"[{p.UploadPath}: {p.FormattedProgress}]"));

        return $"""
                Total Progress: {StringFormatter.FormatProgress(TotalProgress)}
                Completed: {CompletedFiles.Count} / {TotalFiles.Count}
                Remaining: {RemainingFiles.Count}
                Is Completed: {IsCompleted}
                Current Progresses:
                {currentProgressDetails}
                """;
    }
}
