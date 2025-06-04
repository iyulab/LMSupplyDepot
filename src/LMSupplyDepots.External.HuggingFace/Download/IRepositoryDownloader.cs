using System.Collections.Generic;
using System.Threading;

namespace LMSupplyDepots.External.HuggingFace.Download;

/// <summary>
/// Defines methods for downloading Hugging Face repositories.
/// </summary>
public interface IRepositoryDownloader
{
    /// <summary>
    /// Downloads a complete repository with progress tracking.
    /// </summary>
    IAsyncEnumerable<RepoDownloadProgress> DownloadRepositoryAsync(
        string repoId,
        string outputDir,
        bool useSubDir = true,
        CancellationToken cancellationToken = default);
}