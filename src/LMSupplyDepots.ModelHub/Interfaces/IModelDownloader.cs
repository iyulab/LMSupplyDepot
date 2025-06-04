using LMSupplyDepots.External.HuggingFace.Models;

namespace LMSupplyDepots.ModelHub.Interfaces;

/// <summary>
/// Interface for model downloaders that can download models from external sources
/// </summary>
public interface IModelDownloader
{
    /// <summary>
    /// Downloads a model from an external source to local storage
    /// </summary>
    Task<LMModel> DownloadModelAsync(
        string modelId,
        string targetDirectory,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets information about a model from an external source without downloading it
    /// </summary>
    Task<LMModel> GetModelInfoAsync(
        string modelId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets information about a model collection
    /// </summary>
    Task<LMCollection> GetCollectionInfoAsync(
        string collectionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Discovers model collections in an external source
    /// </summary>
    Task<IReadOnlyList<LMCollection>> DiscoverCollectionsAsync(
        ModelType? type = null,
        string? searchTerm = null,
        int limit = 10,
        ModelSortField sort = ModelSortField.Downloads,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the source name for this downloader
    /// </summary>
    string SourceName { get; }

    /// <summary>
    /// Checks if this downloader can handle the given model ID
    /// </summary>
    bool CanHandle(string modelId);

    /// <summary>
    /// Pauses a download that is in progress
    /// </summary>
    Task<bool> PauseDownloadAsync(
        string modelId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes a previously paused download
    /// </summary>
    Task<LMModel> ResumeDownloadAsync(
        string modelId,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a download that is in progress or paused
    /// </summary>
    Task<bool> CancelDownloadAsync(
        string modelId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current status of a download
    /// </summary>
    Task<ModelDownloadStatus?> GetDownloadStatusAsync(
        string modelId,
        CancellationToken cancellationToken = default);
}
