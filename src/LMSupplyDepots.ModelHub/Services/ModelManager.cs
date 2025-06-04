using LMSupplyDepots.External.HuggingFace.Models;

namespace LMSupplyDepots.ModelHub.Services;

/// <summary>
/// Implementation of IModelManager that coordinates model operations.
/// </summary>
public class ModelManager : IModelManager, IDisposable
{
    private readonly ModelHubOptions _options;
    private readonly ILogger<ModelManager> _logger;
    private readonly IModelRepository _repository;
    private readonly FileSystemModelRepository _fileSystemRepository;
    private readonly IEnumerable<IModelDownloader> _downloaders;
    private readonly DownloadManager _downloadManager;
    private readonly ConcurrentDictionary<string, LMCollection> _collectionCache = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public ModelManager(
        IOptions<ModelHubOptions> options,
        ILogger<ModelManager> logger,
        IModelRepository repository,
        FileSystemModelRepository fileSystemRepository,
        IEnumerable<IModelDownloader> downloaders,
        DownloadManager downloadManager)
    {
        _options = options.Value;
        _logger = logger;
        _repository = repository;
        _fileSystemRepository = fileSystemRepository;
        _downloaders = downloaders;
        _downloadManager = downloadManager;
    }

    #region Local Model Management

    public Task<LMModel?> GetModelAsync(string modelId, CancellationToken cancellationToken = default)
    {
        return _repository.GetModelAsync(modelId, cancellationToken);
    }

    public async Task<bool> IsModelDownloadedAsync(string modelId, CancellationToken cancellationToken = default)
    {
        return await _repository.ExistsAsync(modelId, cancellationToken);
    }

    public Task<IReadOnlyList<LMModel>> ListModelsAsync(
        ModelType? type = null,
        string? searchTerm = null,
        int skip = 0,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        return _repository.ListModelsAsync(type, searchTerm, skip, take, cancellationToken);
    }

    public async Task<bool> DeleteModelAsync(string modelId, CancellationToken cancellationToken = default)
    {
        // Cancel any active download first
        await _downloadManager.CancelDownloadAsync(modelId, cancellationToken);
        return await _repository.DeleteModelAsync(modelId, cancellationToken);
    }

    public async Task<LMModel> SetModelAliasAsync(
        string modelId,
        string? alias,
        CancellationToken cancellationToken = default)
    {
        var model = await GetModelAsync(modelId, cancellationToken);
        if (model == null)
        {
            throw new ModelNotFoundException(modelId, "Model not found");
        }

        if (string.IsNullOrEmpty(alias))
        {
            _logger.LogInformation("Clearing alias for model {ModelId}", modelId);
            model.Alias = null;
        }
        else
        {
            var modelWithSameAlias = await GetModelByAliasAsync(alias, cancellationToken);
            if (modelWithSameAlias != null && modelWithSameAlias.Id != modelId)
            {
                throw new InvalidOperationException(
                    $"Alias '{alias}' is already used by model '{modelWithSameAlias.Id}'");
            }

            _logger.LogInformation("Setting alias '{Alias}' for model {ModelId}", alias, modelId);
            model.Alias = alias;
        }

        return await _repository.SaveModelAsync(model, cancellationToken);
    }

    public async Task<LMModel?> GetModelByAliasAsync(
        string alias,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(alias))
        {
            return null;
        }

        var allModels = await _repository.ListModelsAsync(
            null, null, 0, int.MaxValue, cancellationToken);

        return allModels.FirstOrDefault(m =>
            !string.IsNullOrEmpty(m.Alias) &&
            string.Equals(m.Alias, alias, StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region Model Discovery

    public async Task<IReadOnlyList<LMCollection>> DiscoverCollectionsAsync(
        ModelType? type = null,
        string? searchTerm = null,
        int limit = 10,
        ModelSortField sort = ModelSortField.Downloads,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Discovering collections with term: {SearchTerm}, type: {Type}, limit: {Limit}",
            searchTerm, type, limit);

        var results = new List<LMCollection>();
        var tasks = new List<Task<IReadOnlyList<LMCollection>>>();

        foreach (var downloader in _downloaders)
        {
            tasks.Add(DiscoverFromSourceAsync(downloader, type, searchTerm, limit, sort, cancellationToken));
        }

        await Task.WhenAll(tasks);

        foreach (var task in tasks)
        {
            try
            {
                var sourceResults = await task;
                results.AddRange(sourceResults);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting collection discovery results");
            }
        }

        return results
            .GroupBy(c => c.Id)
            .Select(g => g.First())
            .OrderBy(c => c.Name)
            .Take(limit)
            .ToList();
    }

    public async Task<LMCollection> GetCollectionInfoAsync(string collectionId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting collection information for {CollectionId}", collectionId);

        if (_collectionCache.TryGetValue(collectionId, out var cachedCollection))
        {
            return cachedCollection;
        }

        var downloader = GetDownloaderForCollection(collectionId);
        if (downloader == null)
        {
            throw new InvalidOperationException($"No downloader can handle collection ID: {collectionId}");
        }

        var collection = await downloader.GetCollectionInfoAsync(collectionId, cancellationToken);

        _collectionCache[collectionId] = collection;

        return collection;
    }

    #endregion

    #region Model Download Management

    public async Task<LMModel> DownloadModelAsync(
        string modelId,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Check if already exists
        var existingModel = await _repository.GetModelAsync(modelId, cancellationToken);
        if (existingModel != null)
        {
            _logger.LogInformation("Model {ModelId} already exists", modelId);
            return existingModel;
        }

        // Get model info and target directory
        var downloader = GetDownloader(modelId);
        var modelInfo = await downloader.GetModelInfoAsync(modelId, cancellationToken);
        var targetDirectory = _fileSystemRepository.GetModelDirectoryPath(modelInfo.Id, modelInfo.Type);

        // Download using the download manager
        var result = await _downloadManager.DownloadModelAsync(modelId, targetDirectory, progress, cancellationToken);

        // Save to repository
        return await _repository.SaveModelAsync(result, cancellationToken);
    }

    public async Task<bool> PauseDownloadAsync(string modelId, CancellationToken cancellationToken = default)
    {
        return await _downloadManager.PauseDownloadAsync(modelId, cancellationToken);
    }

    /// <summary>
    /// Resumes a paused model download with proper status management
    /// </summary>
    public async Task<LMModel> ResumeDownloadAsync(
        string modelId,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Use the download manager which handles status properly
        var result = await _downloadManager.ResumeDownloadAsync(modelId, progress, cancellationToken);

        // Save the result to repository
        return await _repository.SaveModelAsync(result, cancellationToken);
    }

    public async Task<bool> CancelDownloadAsync(string modelId, CancellationToken cancellationToken = default)
    {
        return await _downloadManager.CancelDownloadAsync(modelId, cancellationToken);
    }

    public ModelDownloadStatus? GetDownloadStatus(string modelId)
    {
        return _downloadManager.GetDownloadStatus(modelId);
    }

    public ModelDownloadProgress? GetDownloadProgress(string modelId)
    {
        return _downloadManager.GetDownloadProgress(modelId);
    }

    public async Task<IEnumerable<DownloadInfo>> GetAllDownloadsAsync(CancellationToken cancellationToken = default)
    {
        var downloads = await _downloadManager.GetAllDownloadsAsync(cancellationToken);

        // Enrich with model information where available
        var enrichedDownloads = new List<DownloadInfo>();

        foreach (var download in downloads)
        {
            try
            {
                if (download.ModelInfo == null)
                {
                    // Try to get model info from repository
                    var model = await _repository.GetModelAsync(download.ModelId, cancellationToken);
                    if (model != null)
                    {
                        download.ModelInfo = model;
                    }
                }

                enrichedDownloads.Add(download);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to enrich download info for model {ModelId}", download.ModelId);
                enrichedDownloads.Add(download); // Add without enrichment
            }
        }

        return enrichedDownloads;
    }

    #endregion

    #region Private Methods

    private async Task<IReadOnlyList<LMCollection>> DiscoverFromSourceAsync(
        IModelDownloader downloader,
        ModelType? type,
        string? searchTerm,
        int limit,
        ModelSortField sort = ModelSortField.Downloads,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var collections = await downloader.DiscoverCollectionsAsync(type, searchTerm, limit, sort, cancellationToken);

            foreach (var collection in collections)
            {
                _collectionCache[collection.Id] = collection;
            }

            return collections;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering collections from {Source}", downloader.SourceName);
            return Array.Empty<LMCollection>();
        }
    }

    private IModelDownloader GetDownloader(string modelId)
    {
        return _downloaders.FirstOrDefault(d => d.CanHandle(modelId))
            ?? throw new ModelSourceNotFoundException(modelId);
    }

    private IModelDownloader? GetDownloaderForCollection(string collectionId)
    {
        string modelId;

        if (collectionId.Contains(':'))
        {
            modelId = collectionId;
        }
        else
        {
            modelId = $"hf:{collectionId}";
        }

        return GetDownloader(modelId);
    }

    #endregion

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _collectionCache.Clear();
            }

            _disposed = true;
        }
    }
}