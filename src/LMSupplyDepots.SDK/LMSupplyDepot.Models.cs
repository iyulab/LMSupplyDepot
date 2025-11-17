using LMSupplyDepots.External.HuggingFace.Models;

namespace LMSupplyDepots.SDK;

/// <summary>
/// Model management functionality for LMSupplyDepot
/// </summary>
public partial class LMSupplyDepot
{
    /// <summary>
    /// Gets the model manager that provides model management capabilities
    /// </summary>
    private IModelManager ModelManager => _serviceProvider.GetRequiredService<IModelManager>();

    #region Local Model Management

    /// <summary>
    /// Lists available models with optional filtering
    /// </summary>
    public Task<IReadOnlyList<LMModel>> ListModelsAsync(
        ModelType? type = null,
        string? searchTerm = null,
        CancellationToken cancellationToken = default)
    {
        return ModelManager.ListModelsAsync(type, searchTerm, 0, 1000, cancellationToken);
    }

    /// <summary>
    /// Gets a model by its ID or alias and updates its actual loading status
    /// </summary>
    public async Task<LMModel?> GetModelAsync(
        string modelKey,
        CancellationToken cancellationToken = default)
    {
        var model = await ModelManager.GetModelAsync(modelKey, cancellationToken);
        if (model != null)
        {
            return model;
        }

        var aliasModel = await ModelManager.GetModelByAliasAsync(modelKey, cancellationToken);
        if (aliasModel != null)
        {
            return aliasModel;
        }

        return null;
    }

    /// <summary>
    /// Gets a model by its alias
    /// </summary>
    public async Task<LMModel?> GetModelByAliasAsync(
        string alias,
        CancellationToken cancellationToken = default)
    {
        var model = await ModelManager.GetModelByAliasAsync(alias, cancellationToken);
        return model;
    }

    /// <summary>
    /// Checks if a model is downloaded
    /// </summary>
    public async Task<bool> IsModelDownloadedAsync(
        string modelKey,
        CancellationToken cancellationToken = default)
    {
        var resolvedId = await ModelManager.ResolveModelKeyAsync(modelKey, cancellationToken);
        return await ModelManager.IsModelDownloadedAsync(resolvedId, cancellationToken);
    }

    /// <summary>
    /// Deletes a model
    /// </summary>
    public async Task<bool> DeleteModelAsync(
        string modelKey,
        CancellationToken cancellationToken = default)
    {
        var resolvedId = await ModelManager.ResolveModelKeyAsync(modelKey, cancellationToken);
        return await ModelManager.DeleteModelAsync(resolvedId, cancellationToken);
    }

    /// <summary>
    /// Sets an alias for a model
    /// </summary>
    public async Task<LMModel> SetModelAliasAsync(
        string modelKey,
        string? alias,
        CancellationToken cancellationToken = default)
    {
        var resolvedId = await ModelManager.ResolveModelKeyAsync(modelKey, cancellationToken);
        return await ModelManager.SetModelAliasAsync(resolvedId, alias, cancellationToken);
    }

    #endregion

    #region Model Discovery

    /// <summary>
    /// Discovers model collections from external hubs
    /// </summary>
    public async Task<IReadOnlyList<LMCollection>> DiscoverCollectionsAsync(
        ModelType? type = null,
        string? searchTerm = null,
        int limit = 10,
        ModelSortField sort = ModelSortField.Downloads,
        CancellationToken cancellationToken = default)
    {
        return await ModelManager.DiscoverCollectionsAsync(type, searchTerm, limit, sort, cancellationToken);
    }

    /// <summary>
    /// Gets information about a model collection from an external hub
    /// </summary>
    public Task<LMCollection> GetCollectionInfoAsync(
        string collectionKey,
        CancellationToken cancellationToken = default)
    {
        return ModelManager.GetCollectionInfoAsync(collectionKey, cancellationToken);
    }

    #endregion

    /// <summary>
    /// Configures ModelHub services
    /// </summary>
    private void ConfigureModelHubServices(IServiceCollection services, string modelsPath)
    {
        services.AddModelHub(options =>
        {
            options.ModelsDirectory = modelsPath;
            options.MaxConcurrentDownloads = _options.MaxConcurrentDownloads;
            options.VerifyChecksums = _options.VerifyChecksums;
            options.MinimumFreeDiskSpace = _options.MinimumFreeDiskSpace;
        });

        services.AddHuggingFaceDownloader(options =>
        {
            options.ApiToken = _options.HuggingFaceApiToken;
            options.MaxConcurrentFileDownloads = _options.MaxConcurrentFileDownloads;
            options.RequestTimeout = _options.HttpRequestTimeout;
            options.MaxRetries = _options.HttpMaxRetries;
        });
    }
}