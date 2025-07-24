using LMSupplyDepots.External.HuggingFace.Models;

namespace LMSupplyDepots.ModelHub.Interfaces;

/// <summary>
/// Defines high-level operations for managing models
/// </summary>
public interface IModelManager
{
    #region Local Model Management

    /// <summary>
    /// Gets a model by its identifier
    /// </summary>
    Task<LMModel?> GetModelAsync(string modelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists models with optional filtering and pagination
    /// </summary>
    Task<IReadOnlyList<LMModel>> ListModelsAsync(
        ModelType? type = null,
        string? searchTerm = null,
        int skip = 0,
        int take = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a model is downloaded
    /// </summary>
    Task<bool> IsModelDownloadedAsync(string modelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a model from the local repository
    /// </summary>
    Task<bool> DeleteModelAsync(string modelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets an alias for a model
    /// </summary>
    Task<LMModel> SetModelAliasAsync(string modelId, string? alias, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a model by its alias
    /// </summary>
    Task<LMModel?> GetModelByAliasAsync(string alias, CancellationToken cancellationToken = default);

    #endregion

    #region Model Discovery

    /// <summary>
    /// Discovers model collections from external hubs
    /// </summary>
    Task<IReadOnlyList<LMCollection>> DiscoverCollectionsAsync(
        ModelType? type = null,
        string? searchTerm = null,
        int limit = 10,
        ModelSortField sort = ModelSortField.Downloads,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets information about a model collection from an external hub
    /// </summary>
    Task<LMCollection> GetCollectionInfoAsync(string collectionId, CancellationToken cancellationToken = default);

    #endregion

    #region Model Download Management

    /// <summary>
    /// Downloads a specific model artifact from an external source
    /// </summary>
    Task<LMModel> DownloadModelAsync(
        string modelId,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pauses a model download
    /// </summary>
    Task<bool> PauseDownloadAsync(string modelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes a paused model download
    /// </summary>
    Task<LMModel> ResumeDownloadAsync(
        string modelId,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a model download
    /// </summary>
    Task<bool> CancelDownloadAsync(string modelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the status of a model download
    /// </summary>
    ModelDownloadStatus? GetDownloadStatus(string modelId);

    /// <summary>
    /// Gets download progress information
    /// </summary>
    ModelDownloadProgress? GetDownloadProgress(string modelId);

    /// <summary>
    /// Gets information about all current downloads
    /// </summary>
    Task<IEnumerable<DownloadInfo>> GetAllDownloadsAsync(CancellationToken cancellationToken = default);

    #endregion
}

public static class ModelManagerExtensions
{
    public static async Task<string> ResolveModelKeyAsync(
            this IModelManager modelManager,
            string modelKey,
            CancellationToken cancellationToken = default)
    {
        var model = await modelManager.GetModelAsync(modelKey, cancellationToken);
        if (model != null)
        {
            return model.Id;
        }

        model = await modelManager.GetModelByAliasAsync(modelKey, cancellationToken);
        if (model != null)
        {
            return model.Id;
        }

        return modelKey;
    }
}