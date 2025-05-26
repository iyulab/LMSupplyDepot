namespace LMSupplyDepots.ModelHub.Repositories;

/// <summary>
/// Implementation of IModelRepository that stores models in the file system
/// </summary>
public class FileSystemModelRepository : IModelRepository, IDisposable
{
    private readonly string _baseDirectory;
    private readonly ILogger<FileSystemModelRepository> _logger;
    private readonly ConcurrentDictionary<string, LMModel> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _initialized;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the FileSystemModelRepository
    /// </summary>
    public FileSystemModelRepository(
        IOptions<ModelHubOptions> options,
        ILogger<FileSystemModelRepository> logger)
    {
        _baseDirectory = options.Value.DataPath;
        _logger = logger;
    }

    /// <summary>
    /// Gets a model by its identifier or alias
    /// </summary>
    public async Task<LMModel?> GetModelAsync(string keyOrId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        // Try direct lookup by ID first
        if (_cache.TryGetValue(keyOrId, out var model))
        {
            return model;
        }

        // Try lookup by alias
        var modelByAlias = _cache.Values.FirstOrDefault(m =>
            !string.IsNullOrEmpty(m.Alias) &&
            string.Equals(m.Alias, keyOrId, StringComparison.OrdinalIgnoreCase));

        if (modelByAlias != null)
        {
            return modelByAlias;
        }

        // Try to parse as a model identifier and load from file
        if (ModelIdentifier.TryParse(keyOrId, out var identifier))
        {
            var metadataFilePath = FileSystemHelper.GetMetadataFilePath(identifier, _baseDirectory);

            if (File.Exists(metadataFilePath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(metadataFilePath, cancellationToken);
                    var loadedModel = JsonHelper.Deserialize<LMModel>(json);

                    if (loadedModel != null)
                    {
                        _cache[loadedModel.Id] = loadedModel;
                        return loadedModel;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load model metadata for {ModelId} from {FilePath}",
                        keyOrId, metadataFilePath);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Lists models with optional filtering and pagination
    /// </summary>
    public async Task<IReadOnlyList<LMModel>> ListModelsAsync(
        ModelType? type = null,
        string? searchTerm = null,
        int skip = 0,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var query = _cache.Values.AsEnumerable();

        if (type.HasValue)
        {
            query = query.Where(m => m.Type == type.Value);
        }

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.ToLowerInvariant();
            query = query.Where(m =>
                m.Id.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                m.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                m.Description.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                m.RepoId.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                m.ArtifactName.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        return query.OrderBy(m => m.Name).Skip(skip).Take(take).ToList();
    }

    /// <summary>
    /// Adds or updates a model in the repository
    /// </summary>
    public async Task<LMModel> SaveModelAsync(LMModel model, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(model.Id))
        {
            throw new ArgumentException("Model ID cannot be empty", nameof(model));
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            // Ensure the model has RepoId, ArtifactName set
            if (string.IsNullOrWhiteSpace(model.RepoId) ||
                string.IsNullOrWhiteSpace(model.ArtifactName))
            {
                // Try to parse from the ID
                if (ModelIdentifier.TryParse(model.Id, out var identifier))
                {
                    identifier.UpdateLMModel(model);
                }
            }

            // Create a ModelIdentifier from the model
            var modelId = ModelIdentifier.FromLMModel(model);

            // Ensure directories exist
            FileSystemHelper.EnsureModelDirectoriesExist(modelId, _baseDirectory);

            // Get metadata file path
            var metadataFilePath = FileSystemHelper.GetMetadataFilePath(modelId, _baseDirectory);

            // Update model's ID if not already in canonical form
            if (model.Id != modelId.ToString())
            {
                model.Id = modelId.ToString();
            }

            // Save metadata
            var json = JsonHelper.Serialize(model);

            await File.WriteAllTextAsync(metadataFilePath, json, cancellationToken);

            // Update the LocalPath 
            if (string.IsNullOrEmpty(model.LocalPath) || !Directory.Exists(model.LocalPath))
            {
                model.LocalPath = FileSystemHelper.GetModelDirectoryPath(modelId, _baseDirectory);
            }

            // Update the cache
            _cache[model.Id] = model;

            _logger.LogInformation("Saved model metadata for {ModelId} to {FilePath}", model.Id, metadataFilePath);

            return model;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Removes a model from the repository
    /// </summary>
    public async Task<bool> DeleteModelAsync(string modelId, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            // Try to get the model first
            var model = await GetModelAsync(modelId, cancellationToken);
            if (model == null)
            {
                return false;
            }

            // Create a ModelIdentifier and get model directory path
            var identifier = ModelIdentifier.FromLMModel(model);
            var modelDirPath = FileSystemHelper.GetModelDirectoryPath(identifier, _baseDirectory);

            // Delete model directory
            if (Directory.Exists(modelDirPath))
            {
                try
                {
                    Directory.Delete(modelDirPath, true);
                    _logger.LogInformation("Deleted model directory: {DirPath}", modelDirPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete model directory for {ModelId}: {DirPath}",
                        modelId, modelDirPath);
                }
            }

            // Remove from cache
            _cache.TryRemove(modelId, out _);

            _logger.LogInformation("Deleted model {ModelId}", modelId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete model {ModelId}", modelId);
            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Checks if a model exists in the repository
    /// </summary>
    public async Task<bool> ExistsAsync(string modelId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        return _cache.ContainsKey(modelId) || await GetModelAsync(modelId, cancellationToken) != null;
    }

    /// <summary>
    /// Gets the path to the model directory (legacy support)
    /// </summary>
    public string GetModelDirectoryPath(string modelId, ModelType modelType)
    {
        return FileSystemHelper.GetModelDirectoryPath(modelId, modelType, _baseDirectory);
    }

    /// <summary>
    /// Initializes the repository by scanning for models
    /// </summary>
    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;

            FileSystemHelper.EnsureBaseDirectoriesExists(_baseDirectory);
            await ScanForModelsAsync(cancellationToken);

            _initialized = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Scans for models by finding all metadata files
    /// </summary>
    private async Task ScanForModelsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Scanning for models in {BaseDir}", _baseDirectory);

        // Use the helper to find all metadata files
        var metadataFiles = FileSystemHelper.FindAllModelMetadataFiles(_baseDirectory);

        foreach (var metadataFile in metadataFiles)
        {
            await LoadModelFromMetadataFileAsync(metadataFile, cancellationToken);
        }

        _logger.LogInformation("Loaded {Count} models from disk", _cache.Count);
    }

    /// <summary>
    /// Loads a model from a metadata file
    /// </summary>
    private async Task LoadModelFromMetadataFileAsync(string metadataFilePath, CancellationToken cancellationToken)
    {
        try
        {
            var json = await File.ReadAllTextAsync(metadataFilePath, cancellationToken);
            var model = JsonHelper.Deserialize<LMModel>(json);

            if (model != null)
            {
                // Ensure the model has a LocalPath
                if (string.IsNullOrEmpty(model.LocalPath))
                {
                    model.LocalPath = Path.GetDirectoryName(metadataFilePath);
                }

                _cache[model.Id] = model;
                _logger.LogDebug("Loaded model {ModelId} from {FilePath}", model.Id, metadataFilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load model metadata from {FilePath}", metadataFilePath);
        }
    }

    /// <summary>
    /// Disposes resources
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Protected implementation of Dispose pattern
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _lock.Dispose();
            }

            _disposed = true;
        }
    }
}