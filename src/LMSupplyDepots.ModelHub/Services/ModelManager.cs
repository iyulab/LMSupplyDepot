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
    private readonly ConcurrentDictionary<string, Task<LMModel>> _activeDownloads = new();
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

    public Task<bool> DeleteModelAsync(string modelId, CancellationToken cancellationToken = default)
    {
        if (_downloadManager.IsDownloading(modelId) || _downloadManager.IsPaused(modelId))
        {
            _downloadManager.CancelDownloadAsync(modelId, cancellationToken).Wait(cancellationToken);
        }

        return _repository.DeleteModelAsync(modelId, cancellationToken);
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
        if (_activeDownloads.TryGetValue(modelId, out var existingTask))
        {
            _logger.LogInformation("Model {ModelId} is already being downloaded, returning existing task", modelId);
            return await existingTask;
        }

        var existingModel = await _repository.GetModelAsync(modelId, cancellationToken);
        if (existingModel != null)
        {
            _logger.LogInformation("Model {ModelId} already exists in the repository", modelId);
            return existingModel;
        }

        var downloader = GetDownloader(modelId);
        if (downloader == null)
        {
            throw new InvalidOperationException($"No downloader can handle model ID: {modelId}");
        }

        var modelInfo = await downloader.GetModelInfoAsync(modelId, cancellationToken);

        var targetDirectory = _fileSystemRepository.GetModelDirectoryPath(modelInfo.Id, modelInfo.Type);

        var downloadTask = StartDownloadAsync(downloader, modelId, modelInfo.Type, targetDirectory, progress, cancellationToken);

        _activeDownloads[modelId] = downloadTask;

        try
        {
            return await downloadTask;
        }
        finally
        {
            _activeDownloads.TryRemove(modelId, out _);
        }
    }

    public async Task<bool> PauseDownloadAsync(string modelId, CancellationToken cancellationToken = default)
    {
        return await _downloadManager.PauseDownloadAsync(modelId, cancellationToken);
    }

    public async Task<ModelDownloadState> ResumeDownloadAsync(
        string modelId,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await _downloadManager.ResumeDownloadAsync(modelId, progress, cancellationToken);
    }

    public async Task<bool> CancelDownloadAsync(string modelId, CancellationToken cancellationToken = default)
    {
        return await _downloadManager.CancelDownloadAsync(modelId, cancellationToken);
    }

    public ModelDownloadStatus? GetDownloadStatus(string modelId)
    {
        return _downloadManager.GetDownloadStatus(modelId);
    }

    public IReadOnlyDictionary<string, ModelDownloadState> GetActiveDownloads()
    {
        return _downloadManager.ActiveDownloads;
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

    private IModelDownloader? GetDownloader(string modelId)
    {
        return _downloaders.FirstOrDefault(d => d.CanHandle(modelId));
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

    private async Task<LMModel> StartDownloadAsync(
        IModelDownloader downloader,
        string modelId,
        ModelType modelType,
        string targetDirectory,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var downloadState = await _downloadManager.StartDownloadAsync(
            modelId,
            modelType,
            targetDirectory,
            progress,
            cancellationToken);

        var complete = false;
        LMModel? result = null;
        Exception? error = null;

        using var timer = new System.Timers.Timer(500);
        using var waitHandle = new ManualResetEvent(false);

        timer.Elapsed += async (sender, e) =>
        {
            try
            {
                var state = _downloadManager.GetDownloadState(modelId);

                if (state == null ||
                    state.Status == ModelDownloadStatus.Completed ||
                    state.Status == ModelDownloadStatus.Failed ||
                    state.Status == ModelDownloadStatus.Cancelled)
                {
                    if (state?.Status == ModelDownloadStatus.Completed)
                    {
                        result = await _repository.GetModelAsync(modelId, cancellationToken);

                        if (result == null)
                        {
                            try
                            {
                                var modelFiles = Directory.GetFiles(targetDirectory, "*.gguf");
                                if (modelFiles.Length > 0)
                                {
                                    var mainModelFile = modelFiles.OrderByDescending(f => new FileInfo(f).Length).First();
                                    var jsonFile = Path.ChangeExtension(mainModelFile, ".json");

                                    if (File.Exists(jsonFile))
                                    {
                                        var json = await File.ReadAllTextAsync(jsonFile, cancellationToken);
                                        result = JsonHelper.Deserialize<LMModel>(json);

                                        if (result != null)
                                        {
                                            result = await _repository.SaveModelAsync(result, cancellationToken);
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error loading model files for {ModelId}", modelId);
                                error = ex;
                            }
                        }
                    }
                    else if (state?.Status == ModelDownloadStatus.Failed)
                    {
                        string errorMessage = state.Message ?? "Unknown error";

                        if (errorMessage.Contains("authentication", StringComparison.OrdinalIgnoreCase) ||
                            errorMessage.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) ||
                            errorMessage.Contains("API token", StringComparison.OrdinalIgnoreCase))
                        {
                            error = new ModelDownloadException(
                                modelId,
                                "This model requires authentication. Please provide a valid API token in the settings.");
                        }
                        else
                        {
                            error = new ModelDownloadException(modelId, errorMessage);
                        }
                    }
                    else if (state?.Status == ModelDownloadStatus.Cancelled)
                    {
                        error = new OperationCanceledException($"Download cancelled for model {modelId}");
                    }

                    complete = true;
                    waitHandle.Set();
                    timer.Stop();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking download status for {ModelId}", modelId);
                error = ex;
                complete = true;
                waitHandle.Set();
                timer.Stop();
            }
        };

        timer.Start();

        await Task.Run(() => waitHandle.WaitOne(), cancellationToken);

        if (error != null)
        {
            throw error;
        }

        if (result == null)
        {
            throw new InvalidOperationException($"Failed to download or load model {modelId}");
        }

        return result;
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
                foreach (var modelId in _activeDownloads.Keys)
                {
                    _downloadManager.CancelDownloadAsync(modelId).Wait();
                }

                _activeDownloads.Clear();
                _collectionCache.Clear();
            }

            _disposed = true;
        }
    }
}