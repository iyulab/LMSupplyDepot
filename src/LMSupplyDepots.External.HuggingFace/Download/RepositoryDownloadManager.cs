using LMSupplyDepots.External.HuggingFace.Client;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace LMSupplyDepots.External.HuggingFace.Download;

/// <summary>
/// Manages repository download operations with concurrent file downloads and progress tracking.
/// </summary>
public sealed class RepositoryDownloadManager
{
    private readonly IHuggingFaceClient _client;
    private readonly ILogger<RepositoryDownloadManager>? _logger;
    private readonly int _maxConcurrentDownloads;
    private readonly int _progressUpdateInterval;

    /// <summary>
    /// Initializes a new instance of the RepositoryDownloadManager.
    /// </summary>
    public RepositoryDownloadManager(
        IHuggingFaceClient client,
        ILogger<RepositoryDownloadManager>? logger = null,
        int maxConcurrentDownloads = 5,
        int progressUpdateInterval = 100)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = logger;
        _maxConcurrentDownloads = maxConcurrentDownloads;
        _progressUpdateInterval = progressUpdateInterval;

        _logger?.LogInformation(
            "RepositoryDownloadManager initialized with maxConcurrentDownloads={MaxConcurrent}, updateInterval={Interval}ms",
            maxConcurrentDownloads, progressUpdateInterval);
    }

    /// <summary>
    /// Downloads all files from a repository with concurrent downloads and progress tracking.
    /// </summary>
    public IAsyncEnumerable<RepoDownloadProgress> DownloadRepositoryAsync(
        string repoId,
        string outputDir,
        bool useSubDir = true,
        CancellationToken cancellationToken = default)
    {
        return DownloadRepositoryFilesAsync(repoId, null, outputDir, useSubDir, cancellationToken);
    }

    /// <summary>
    /// Downloads specific files from a repository with concurrent downloads and progress tracking.
    /// </summary>
    public async IAsyncEnumerable<RepoDownloadProgress> DownloadRepositoryFilesAsync(
        string repoId,
        IEnumerable<string>? specificFiles,
        string outputDir,
        bool useSubDir = true,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoId);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDir);

        _logger?.LogInformation("Starting repository download: {RepoId} to {OutputDir}", repoId, outputDir);

        // Get repository information
        var model = await _client.FindModelByRepoIdAsync(repoId, cancellationToken);

        // Get files to download
        var files = specificFiles?.ToArray() ?? model.GetFilePaths();

        if (files.Length == 0)
        {
            _logger?.LogWarning("No files found to download in repository: {RepoId}", repoId);
            yield break;
        }

        // Prepare output directory
        var targetDir = useSubDir ? Path.Combine(outputDir, repoId.Replace('/', '_')) : outputDir;
        Directory.CreateDirectory(targetDir);

        var progress = RepoDownloadProgress.Create(files);
        var downloadProgresses = new ConcurrentDictionary<string, FileDownloadProgress>();
        var completedFiles = new ConcurrentBag<string>();
        var failedFiles = new ConcurrentBag<(string File, Exception Error)>();

        // Create download tasks for each file
        using var semaphore = new SemaphoreSlim(_maxConcurrentDownloads);
        var downloadTasks = new List<Task>();

        foreach (var file in files)
        {
            var task = ProcessFileDownloadAsync(
                repoId,
                file,
                targetDir,
                semaphore,
                downloadProgresses,
                completedFiles,
                failedFiles,
                cancellationToken);

            downloadTasks.Add(task);
        }

        // Monitor progress
        while (!downloadTasks.All(t => t.IsCompleted))
        {
            // 인증 오류 발생 시 즉시 중단
            var authError = failedFiles.FirstOrDefault(f =>
                f.Error is HuggingFaceException hfe &&
                hfe.StatusCode == System.Net.HttpStatusCode.Unauthorized);

            if (authError != default)
            {
                throw authError.Error;
            }

            var currentProgress = progress with
            {
                CompletedFiles = completedFiles.ToImmutableHashSet(),
                CurrentProgresses = downloadProgresses.Values.ToImmutableList()
            };

            yield return currentProgress;
            await Task.Delay(_progressUpdateInterval, cancellationToken);
        }

        // Check for any failed downloads
        if (failedFiles.Count > 0)
        {
            var firstError = failedFiles.First();
            throw new HuggingFaceException(
                $"Failed to download files. First error ({firstError.File}): {firstError.Error.Message}",
                (firstError.Error as HuggingFaceException)?.StatusCode ?? System.Net.HttpStatusCode.InternalServerError,
                firstError.Error);
        }

        _logger?.LogInformation("Repository download completed: {RepoId}", repoId);
        yield return progress.AsCompleted();
    }

    private async Task ProcessFileDownloadAsync(
        string repoId,
        string filePath,
        string targetDir,
        SemaphoreSlim semaphore,
        ConcurrentDictionary<string, FileDownloadProgress> progresses,
        ConcurrentBag<string> completedFiles,
        ConcurrentBag<(string File, Exception Error)> failedFiles,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);

        try
        {
            var outputPath = Path.Combine(targetDir, filePath);
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            _logger?.LogInformation("Starting file download: {FilePath}", filePath);

            var progress = new Progress<FileDownloadProgress>(p =>
            {
                if (p.IsCompleted)
                {
                    completedFiles.Add(filePath);
                    progresses.TryRemove(filePath, out _);
                    _logger?.LogInformation("File download completed: {FilePath}", filePath);
                }
                else
                {
                    progresses[filePath] = p;
                }
            });

            var result = await _client.DownloadFileWithResultAsync(
                repoId, filePath, outputPath,
                progress: progress,
                cancellationToken: cancellationToken);

            if (result.IsCompleted)
            {
                completedFiles.Add(filePath);
                progresses.TryRemove(filePath, out _);
                _logger?.LogInformation("File download completed: {FilePath}", filePath);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error downloading file: {FilePath}", filePath);
            failedFiles.Add((filePath, ex));
        }
        finally
        {
            semaphore.Release();
        }
    }
}