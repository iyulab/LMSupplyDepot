using LMSupplyDepots.External.HuggingFace.Download;
using LMSupplyDepots.External.HuggingFace.Models;

namespace LMSupplyDepots.External.HuggingFace.Client;

/// <summary>
/// Represents the core functionality for interacting with the Hugging Face API.
/// </summary>
public interface IHuggingFaceClient : IDisposable
{
    /// <summary>
    /// Asynchronously searches for text generation models.
    /// </summary>
    Task<IReadOnlyList<HuggingFaceModel>> SearchTextGenerationModelsAsync(
        string? search = null,
        string[]? filters = null,
        int limit = 5,
        ModelSortField sortField = ModelSortField.Downloads,
        bool descending = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously searches for embedding models.
    /// </summary>
    Task<IReadOnlyList<HuggingFaceModel>> SearchEmbeddingModelsAsync(
        string? search = null,
        string[]? filters = null,
        int limit = 5,
        ModelSortField sortField = ModelSortField.Downloads,
        bool descending = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously finds a model by its repository ID.
    /// </summary>
    Task<HuggingFaceModel> FindModelByRepoIdAsync(
        string repoId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously retrieves file information from a repository.
    /// </summary>
    Task<HuggingFaceFile> GetFileInfoAsync(
        string repoId,
        string filePath,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RepositoryFileInfo>> GetRepositoryFilesAsync(
        string repoId,
        string? treePath = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously downloads all files from a repository.
    /// </summary>
    IAsyncEnumerable<RepoDownloadProgress> DownloadRepositoryAsync(
        string repoId,
        string outputDir,
        bool useSubDir = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously downloads specified files from a repository.
    /// </summary>
    IAsyncEnumerable<RepoDownloadProgress> DownloadRepositoryFilesAsync(
        string repoId,
        IEnumerable<string> filePaths,
        string outputDir,
        bool useSubDir = true,
        CancellationToken cancellationToken = default);

    Task<Dictionary<string, long>> GetRepositoryFileSizesAsync(
        string repoId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously downloads a file from a repository, using a single operation.
    /// </summary>
    Task<FileDownloadResult> DownloadFileWithResultAsync(
        string repoId,
        string filePath,
        string outputPath,
        long startFrom = 0,
        IProgress<FileDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the download URL for a specific file in a repository.
    /// </summary>
    string? GetDownloadUrl(string repoId, string filePath);
}