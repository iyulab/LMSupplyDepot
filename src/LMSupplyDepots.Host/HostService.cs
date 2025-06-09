using LMSupplyDepots.External.HuggingFace.Models;

namespace LMSupplyDepots.Host;

/// <summary>
/// Hosting service that delegates functionality to LMSupplyDepot SDK
/// </summary>
internal class HostService : IHostService, IAsyncDisposable
{
    private readonly ILogger<HostService> _logger;
    private readonly LMSupplyDepot _depot;
    private bool _disposed;

    public HostService(ILogger<HostService> logger, IOptions<LMSupplyDepotOptions> options)
    {
        _logger = logger;
        var depotOptions = options.Value;
        _depot = new LMSupplyDepot(depotOptions, CreateLoggerFactory(logger));
        _logger.LogInformation("LMSupplyDepots Host Service initialized with models directory: {ModelsDirectory}", depotOptions.DataPath);
    }

    #region Local Model Management

    public Task<IReadOnlyList<LMModel>> ListModelsAsync(ModelType? type = null, string? searchTerm = null, CancellationToken cancellationToken = default)
        => _depot.ListModelsAsync(type, searchTerm, cancellationToken);

    public Task<LMModel?> GetModelAsync(string modelId, CancellationToken cancellationToken = default)
        => _depot.GetModelAsync(modelId, cancellationToken);

    public Task<LMModel?> GetModelByAliasAsync(string alias, CancellationToken cancellationToken = default)
        => _depot.GetModelByAliasAsync(alias, cancellationToken);

    public Task<bool> IsModelDownloadedAsync(string modelId, CancellationToken cancellationToken = default)
        => _depot.IsModelDownloadedAsync(modelId, cancellationToken);

    public Task<bool> DeleteModelAsync(string modelId, CancellationToken cancellationToken = default)
        => _depot.DeleteModelAsync(modelId, cancellationToken);

    public Task<LMModel> SetModelAliasAsync(string modelId, string? alias, CancellationToken cancellationToken = default)
        => _depot.SetModelAliasAsync(modelId, alias, cancellationToken);

    #endregion

    #region Collection Discovery

    public Task<IReadOnlyList<LMCollection>> DiscoverCollectionsAsync(ModelType? type = null, string? searchTerm = null, int limit = 10, ModelSortField sort = ModelSortField.Downloads, CancellationToken cancellationToken = default)
        => _depot.DiscoverCollectionsAsync(type, searchTerm, limit, sort, cancellationToken);

    public Task<LMCollection> GetCollectionInfoAsync(string collectionId, CancellationToken cancellationToken = default)
        => _depot.GetCollectionInfoAsync(collectionId, cancellationToken);

    #endregion

    #region Model Download Management

    public Task<ModelDownloadProgress?> GetDownloadProgressAsync(string modelKey, CancellationToken cancellationToken = default)
        => _depot.GetDownloadProgressAsync(modelKey, cancellationToken);

    public Task<IEnumerable<DownloadInfo>> GetAllDownloadsAsync(CancellationToken cancellationToken = default)
        => _depot.GetAllDownloadsAsync(cancellationToken);

    /// <summary>
    /// Starts a download with proper validation and returns operation result
    /// </summary>
    public async Task<DownloadOperationResult> StartDownloadAsync(string modelKey, CancellationToken cancellationToken = default)
    {
        try
        {
            // Check current status
            var currentProgress = await _depot.GetDownloadProgressAsync(modelKey, cancellationToken);

            if (currentProgress?.Status == ModelDownloadStatus.Downloading)
            {
                return DownloadOperationResult.CreateFailure(
                    $"Model {modelKey} is already being downloaded",
                    "AlreadyDownloading");
            }

            if (currentProgress?.Status == ModelDownloadStatus.Completed)
            {
                return DownloadOperationResult.CreateSuccess(
                    $"Model {modelKey} is already downloaded",
                    "AlreadyCompleted",
                    currentProgress);
            }

            // Start download in background
            _ = Task.Run(async () =>
            {
                try
                {
                    await _depot.DownloadModelAsync(modelKey, null, CancellationToken.None);
                    _logger.LogInformation("Background download completed for model {Model}", modelKey);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Background download failed for model {Model}", modelKey);
                }
            });

            return DownloadOperationResult.CreateSuccess(
                $"Download started for model {modelKey}",
                "Started");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting download for model {Model}", modelKey);
            return DownloadOperationResult.CreateFailure(ex.Message);
        }
    }

    /// <summary>
    /// Pauses a download with proper validation and returns operation result
    /// </summary>
    public async Task<DownloadOperationResult> PauseDownloadAsync(string modelKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var currentProgress = await _depot.GetDownloadProgressAsync(modelKey, cancellationToken);

            if (currentProgress == null)
            {
                return DownloadOperationResult.CreateFailure(
                    $"No download found for model {modelKey}",
                    "NotFound");
            }

            if (currentProgress.Status == ModelDownloadStatus.Completed)
            {
                return DownloadOperationResult.CreateSuccess(
                    $"Model {modelKey} is already completed",
                    "AlreadyCompleted",
                    currentProgress);
            }

            if (currentProgress.Status == ModelDownloadStatus.Paused)
            {
                return DownloadOperationResult.CreateSuccess(
                    $"Model {modelKey} is already paused",
                    "AlreadyPaused",
                    currentProgress);
            }

            if (currentProgress.Status != ModelDownloadStatus.Downloading)
            {
                return DownloadOperationResult.CreateFailure(
                    $"Cannot pause model {modelKey} - current status: {currentProgress.Status}",
                    "InvalidState");
            }

            // Actually pause the download
            bool result = await _depot.PauseDownloadAsync(modelKey, cancellationToken);

            if (result)
            {
                // Get updated progress
                var updatedProgress = await _depot.GetDownloadProgressAsync(modelKey, cancellationToken);
                return DownloadOperationResult.CreateSuccess(
                    $"Download paused for model {modelKey}",
                    "Paused",
                    updatedProgress);
            }
            else
            {
                return DownloadOperationResult.CreateFailure(
                    $"Failed to pause download for model {modelKey}",
                    "PauseFailed");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pausing download for model {Model}", modelKey);
            return DownloadOperationResult.CreateFailure(ex.Message);
        }
    }

    /// <summary>
    /// Resumes a download with proper validation and returns operation result
    /// </summary>
    public async Task<DownloadOperationResult> ResumeDownloadAsync(string modelKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var currentProgress = await _depot.GetDownloadProgressAsync(modelKey, cancellationToken);

            if (currentProgress == null)
            {
                return DownloadOperationResult.CreateFailure(
                    $"No download found for model {modelKey}",
                    "NotFound");
            }

            if (currentProgress.Status == ModelDownloadStatus.Completed)
            {
                return DownloadOperationResult.CreateSuccess(
                    $"Model {modelKey} is already completed",
                    "AlreadyCompleted",
                    currentProgress);
            }

            if (currentProgress.Status == ModelDownloadStatus.Downloading)
            {
                return DownloadOperationResult.CreateSuccess(
                    $"Model {modelKey} is already downloading",
                    "AlreadyDownloading",
                    currentProgress);
            }

            if (currentProgress.Status != ModelDownloadStatus.Paused)
            {
                return DownloadOperationResult.CreateFailure(
                    $"Cannot resume model {modelKey} - current status: {currentProgress.Status}",
                    "InvalidState");
            }

            // Resume download in background
            _ = Task.Run(async () =>
            {
                try
                {
                    await _depot.ResumeDownloadAsync(modelKey, null, CancellationToken.None);
                    _logger.LogInformation("Background resume completed for model {Model}", modelKey);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Background resume failed for model {Model}", modelKey);
                }
            });

            return DownloadOperationResult.CreateSuccess(
                $"Download resumed for model {modelKey}",
                "Resumed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resuming download for model {Model}", modelKey);
            return DownloadOperationResult.CreateFailure(ex.Message);
        }
    }

    /// <summary>
    /// Cancels a download with proper validation and returns operation result
    /// </summary>
    public async Task<DownloadOperationResult> CancelDownloadAsync(string modelKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var currentProgress = await _depot.GetDownloadProgressAsync(modelKey, cancellationToken);

            if (currentProgress == null)
            {
                return DownloadOperationResult.CreateFailure(
                    $"No download found for model {modelKey}",
                    "NotFound");
            }

            if (currentProgress.Status == ModelDownloadStatus.Completed)
            {
                return DownloadOperationResult.CreateSuccess(
                    $"Model {modelKey} is already completed",
                    "AlreadyCompleted",
                    currentProgress);
            }

            // Actually cancel the download
            bool result = await _depot.CancelDownloadAsync(modelKey, cancellationToken);

            if (result)
            {
                return DownloadOperationResult.CreateSuccess(
                    $"Download cancelled for model {modelKey}",
                    "Cancelled");
            }
            else
            {
                return DownloadOperationResult.CreateFailure(
                    $"Failed to cancel download for model {modelKey}",
                    "CancelFailed");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling download for model {Model}", modelKey);
            return DownloadOperationResult.CreateFailure(ex.Message);
        }
    }

    #endregion

    #region Model Loading and Inference

    public Task<bool> IsModelLoadedAsync(string modelId, CancellationToken cancellationToken = default)
        => _depot.IsModelLoadedAsync(modelId, cancellationToken);

    public Task<IReadOnlyList<LMModel>> GetLoadedModelsAsync(CancellationToken cancellationToken = default)
        => _depot.GetLoadedModelsAsync(cancellationToken);

    public Task<LMModel> LoadModelAsync(string modelId, Dictionary<string, object?>? parameters = null, CancellationToken cancellationToken = default)
        => _depot.LoadModelAsync(modelId, parameters, cancellationToken);

    public Task UnloadModelAsync(string modelId, CancellationToken cancellationToken = default)
        => _depot.UnloadModelAsync(modelId, cancellationToken);

    #endregion

    #region Text Generation

    public Task<GenerationResponse> GenerateTextAsync(string modelId, GenerationRequest request, CancellationToken cancellationToken = default)
        => _depot.GenerateTextAsync(modelId, request, cancellationToken);

    public async IAsyncEnumerable<string> GenerateTextStreamAsync(
        string modelId,
        GenerationRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        request.Stream = true;
        await foreach (var token in _depot.GenerateTextStreamAsync(
            modelId, request.Prompt, request.MaxTokens, request.Temperature, request.TopP, request.Parameters, cancellationToken))
        {
            yield return token;
        }
    }

    public Task<GenerationResponse> GenerateTextAsync(
        string modelId, string prompt, int maxTokens = 256, float temperature = 0.7f, float topP = 0.95f,
        Dictionary<string, object?>? parameters = null, CancellationToken cancellationToken = default)
        => _depot.GenerateTextAsync(modelId, prompt, maxTokens, temperature, topP, parameters, cancellationToken);

    #endregion

    #region Embeddings

    public Task<EmbeddingResponse> GenerateEmbeddingsAsync(string modelId, EmbeddingRequest request, CancellationToken cancellationToken = default)
        => _depot.GenerateEmbeddingsAsync(modelId, request, cancellationToken);

    public Task<EmbeddingResponse> GenerateEmbeddingsAsync(
        string modelId, IReadOnlyList<string> texts, bool normalize = false,
        Dictionary<string, object?>? parameters = null, CancellationToken cancellationToken = default)
        => _depot.GenerateEmbeddingsAsync(modelId, texts, normalize, parameters, cancellationToken);

    #endregion

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        if (_depot is IDisposable disposableDepot)
            disposableDepot.Dispose();

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private static ILoggerFactory CreateLoggerFactory(ILogger logger)
        => LoggerFactory.Create(builder => builder.AddProvider(new ForwardingLoggerProvider(logger)));
}

/// <summary>
/// Logger provider that forwards to another logger
/// </summary>
internal class ForwardingLoggerProvider : ILoggerProvider
{
    private readonly ILogger _logger;

    public ForwardingLoggerProvider(ILogger logger) => _logger = logger;
    public ILogger CreateLogger(string categoryName) => _logger;
    public void Dispose() { }
}