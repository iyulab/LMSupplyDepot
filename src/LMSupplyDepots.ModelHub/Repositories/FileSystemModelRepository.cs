namespace LMSupplyDepots.ModelHub.Repositories;

/// <summary>
/// Implementation of IModelRepository that directly scans the file system without caching
/// </summary>
public class FileSystemModelRepository : IModelRepository, IDisposable
{
    private readonly string _baseDirectory;
    private readonly ILogger<FileSystemModelRepository> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
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

        _logger.LogInformation("FileSystemModelRepository initialized with DataPath: {DataPath}", _baseDirectory);

        // Ensure base directories exist
        FileSystemHelper.EnsureBaseDirectoriesExists(_baseDirectory);
    }

    /// <summary>
    /// Gets a model by its identifier or alias - scans file system directly
    /// </summary>
    public async Task<LMModel?> GetModelAsync(string keyOrId, CancellationToken cancellationToken = default)
    {
        // First try to find by ID
        var model = await FindModelByIdAsync(keyOrId, cancellationToken);
        if (model != null)
        {
            return model;
        }

        // Then try to find by alias by scanning all models
        var allModels = await ScanAllModelsAsync(cancellationToken);
        var modelByAlias = allModels.FirstOrDefault(m =>
            !string.IsNullOrEmpty(m.Alias) &&
            string.Equals(m.Alias, keyOrId, StringComparison.OrdinalIgnoreCase));

        return modelByAlias;
    }

    /// <summary>
    /// Lists models by directly scanning the file system
    /// </summary>
    public async Task<IReadOnlyList<LMModel>> ListModelsAsync(
        ModelType? type = null,
        string? searchTerm = null,
        int skip = 0,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        var allModels = await ScanAllModelsAsync(cancellationToken);

        var query = allModels.AsEnumerable();

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

            _logger.LogInformation("Saved model metadata for {ModelId} to {FilePath}", model.Id, metadataFilePath);

            return model;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Removes a model from the repository by deleting its files
    /// </summary>
    public async Task<bool> DeleteModelAsync(string modelId, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            // Try to get the model first
            var model = await FindModelByIdAsync(modelId, cancellationToken);
            if (model == null)
            {
                // Also try by alias
                var allModels = await ScanAllModelsAsync(cancellationToken);
                model = allModels.FirstOrDefault(m =>
                    !string.IsNullOrEmpty(m.Alias) &&
                    string.Equals(m.Alias, modelId, StringComparison.OrdinalIgnoreCase));

                if (model == null)
                {
                    return false;
                }
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
    /// Checks if a model exists by scanning the file system
    /// </summary>
    public async Task<bool> ExistsAsync(string modelId, CancellationToken cancellationToken = default)
    {
        var model = await GetModelAsync(modelId, cancellationToken);
        return model != null;
    }

    /// <summary>
    /// Gets the path to the model directory (legacy support)
    /// </summary>
    public string GetModelDirectoryPath(string modelId, ModelType modelType)
    {
        return FileSystemHelper.GetModelDirectoryPath(modelId, modelType, _baseDirectory);
    }

    /// <summary>
    /// Scans all models in the file system
    /// </summary>
    private async Task<List<LMModel>> ScanAllModelsAsync(CancellationToken cancellationToken)
    {
        var models = new List<LMModel>();

        try
        {
            _logger.LogDebug("Scanning models in {BaseDir}", _baseDirectory);

            // Use the helper to find all metadata files
            var metadataFiles = FileSystemHelper.FindAllModelMetadataFiles(_baseDirectory);

            foreach (var metadataFile in metadataFiles)
            {
                var model = await LoadModelFromMetadataFileAsync(metadataFile, cancellationToken);
                if (model != null)
                {
                    models.Add(model);
                }
            }

            _logger.LogDebug("Found {Count} models in file system", models.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning models from file system");
        }

        return models;
    }

    /// <summary>
    /// Finds a model by its ID by scanning the file system
    /// </summary>
    private async Task<LMModel?> FindModelByIdAsync(string modelId, CancellationToken cancellationToken)
    {
        try
        {
            // Try to parse as a model identifier and load from file
            if (ModelIdentifier.TryParse(modelId, out var identifier))
            {
                var metadataFilePath = FileSystemHelper.GetMetadataFilePath(identifier, _baseDirectory);

                if (File.Exists(metadataFilePath))
                {
                    return await LoadModelFromMetadataFileAsync(metadataFilePath, cancellationToken);
                }
            }

            // If direct lookup fails, scan all models (this handles cases where the ID format might be different)
            var allModels = await ScanAllModelsAsync(cancellationToken);
            return allModels.FirstOrDefault(m => m.Id.Equals(modelId, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding model by ID {ModelId}", modelId);
            return null;
        }
    }

    /// <summary>
    /// Loads a model from a metadata file
    /// </summary>
    private async Task<LMModel?> LoadModelFromMetadataFileAsync(string metadataFilePath, CancellationToken cancellationToken)
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

                // Verify that the model directory and files actually exist
                if (!string.IsNullOrEmpty(model.LocalPath) && Directory.Exists(model.LocalPath))
                {
                    // Check if the directory contains actual model files
                    if (FileSystemHelper.ContainsModelFiles(model.LocalPath) ||
                        model.FilePaths.Any(fp => File.Exists(Path.Combine(model.LocalPath, fp))))
                    {
                        _logger.LogDebug("Loaded model {ModelId} from {FilePath}", model.Id, metadataFilePath);
                        return model;
                    }
                    else
                    {
                        _logger.LogWarning("Model {ModelId} metadata exists but no model files found in {LocalPath}",
                            model.Id, model.LocalPath);
                    }
                }
                else
                {
                    _logger.LogWarning("Model {ModelId} metadata exists but directory {LocalPath} not found",
                        model.Id, model.LocalPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load model metadata from {FilePath}", metadataFilePath);
        }

        return null;
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