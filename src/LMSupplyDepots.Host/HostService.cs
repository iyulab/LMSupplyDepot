using LMSupplyDepots.External.HuggingFace.Models;
using LMSupplyDepots.ModelHub;
using LMSupplyDepots.Inference;
using LMSupplyDepots.Interfaces;
using LMSupplyDepots.SDK;

namespace LMSupplyDepots.Host;

/// <summary>
/// Hosting service that is a thin wrapper around LMSupplyDepot SDK
/// </summary>
internal class HostService : IHostService, IAsyncDisposable
{
    private readonly ILogger<HostService> _logger;
    private readonly LMSupplyDepot _depot;
    private bool _disposed;

    public HostService(
        ILogger<HostService> logger,
        LMSupplyDepot depot)
    {
        _logger = logger;
        _depot = depot;
        _logger.LogInformation("LMSupplyDepots Host Service initialized as thin wrapper around LMSupplyDepot SDK");
    }

    #region Local Model Management

    public Task<IReadOnlyList<LMModel>> ListModelsAsync(ModelType? type = null, string? searchTerm = null, CancellationToken cancellationToken = default)
        => _depot.ListModelsAsync(type, searchTerm, cancellationToken);

    public Task<LMModel?> GetModelAsync(string modelId, CancellationToken cancellationToken = default)
        => _depot.GetModelAsync(modelId, cancellationToken);

    public Task<LMModel?> GetModelByAliasAsync(string alias, CancellationToken cancellationToken = default)
        => _depot.GetModelAsync(alias, cancellationToken);

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

    public async Task<DownloadOperationResult> StartDownloadAsync(string modelKey, CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if download is already in progress
            var currentProgress = await _depot.GetDownloadProgressAsync(modelKey, cancellationToken);
            if (currentProgress?.Status == ModelDownloadStatus.Downloading)
            {
                return DownloadOperationResult.CreateFailure($"Download already in progress for {modelKey}", "AlreadyInProgress");
            }

            // Start download in background - fire and forget
            _ = Task.Run(async () =>
            {
                try
                {
                    _logger.LogInformation("Starting background download for {ModelKey}", modelKey);
                    await _depot.DownloadModelAsync(modelKey, null, CancellationToken.None);
                    _logger.LogInformation("Background download completed successfully for {ModelKey}", modelKey);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Background download failed for {ModelKey}", modelKey);
                }
            }, CancellationToken.None);

            // Return immediately with success status
            return DownloadOperationResult.CreateSuccess($"Download started for {modelKey}", "Started");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initiate download for {ModelKey}", modelKey);
            return DownloadOperationResult.CreateFailure($"Failed to start download: {ex.Message}", "Failed");
        }
    }

    public async Task<DownloadOperationResult> PauseDownloadAsync(string modelKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _depot.PauseDownloadAsync(modelKey, cancellationToken);
            var progress = await _depot.GetDownloadProgressAsync(modelKey, cancellationToken);
            var status = result ? "Paused" : "Failed to pause";
            var message = result ? $"Download paused for {modelKey}" : $"Failed to pause download for {modelKey}";
            return result
                ? DownloadOperationResult.CreateSuccess(message, status, progress)
                : DownloadOperationResult.CreateFailure(message, status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pause download for {ModelKey}", modelKey);
            return DownloadOperationResult.CreateFailure($"Failed to pause download: {ex.Message}", "Failed");
        }
    }

    public async Task<DownloadOperationResult> ResumeDownloadAsync(string modelKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _depot.ResumeDownloadAsync(modelKey, null, cancellationToken);
            var progress = await _depot.GetDownloadProgressAsync(modelKey, cancellationToken);
            return DownloadOperationResult.CreateSuccess($"Download resumed for {modelKey}", "Resumed", progress);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resume download for {ModelKey}", modelKey);
            return DownloadOperationResult.CreateFailure($"Failed to resume download: {ex.Message}", "Failed");
        }
    }

    public async Task<DownloadOperationResult> CancelDownloadAsync(string modelKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _depot.CancelDownloadAsync(modelKey, cancellationToken);
            var status = result ? "Cancelled" : "Failed to cancel";
            var message = result ? $"Download cancelled for {modelKey}" : $"Failed to cancel download for {modelKey}";
            return result
                ? DownloadOperationResult.CreateSuccess(message, status)
                : DownloadOperationResult.CreateFailure(message, status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel download for {ModelKey}", modelKey);
            return DownloadOperationResult.CreateFailure($"Failed to cancel download: {ex.Message}", "Failed");
        }
    }

    #endregion

    #region Model Loading and Inference

    public Task<bool> IsModelLoadedAsync(string modelId, CancellationToken cancellationToken = default)
        => _depot.IsModelLoadedAsync(modelId, cancellationToken);

    public Task<IReadOnlyList<LMModel>> GetLoadedModelsAsync(CancellationToken cancellationToken = default)
        => _depot.GetLoadedModelsAsync(cancellationToken);

    public Task<LMModel> LoadModelAsync(string modelId, Dictionary<string, object?>? parameters = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("HostService.LoadModelAsync called for model: {ModelId}", modelId);
        return _depot.LoadModelAsync(modelId, parameters, cancellationToken);
    }

    public Task UnloadModelAsync(string modelId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("HostService.UnloadModelAsync called for model: {ModelId}", modelId);
        return _depot.UnloadModelAsync(modelId, cancellationToken);
    }

    #endregion

    #region Text Generation

    public async Task<GenerationResponse> GenerateTextAsync(string modelId, GenerationRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Generating text for model {ModelId}", modelId);
            _logger.LogDebug("Generation request: Prompt={Prompt}, MaxTokens={MaxTokens}, Temperature={Temperature}",
                request.Prompt, request.MaxTokens, request.Temperature);

            // Use the LMSupplyDepot to generate text
            var response = await _depot.GenerateTextAsync(modelId, request, cancellationToken);

            _logger.LogDebug("Text generation completed for model {ModelId}", modelId);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate text for model {ModelId}. Request: {Request}", modelId,
                System.Text.Json.JsonSerializer.Serialize(request));
            throw;
        }
    }
    public async IAsyncEnumerable<string> GenerateTextStreamAsync(
        string modelId,
        GenerationRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Starting text generation stream for model {ModelId}", modelId);

        await foreach (var token in _depot.GenerateTextStreamAsync(modelId, request.Prompt ?? "", request.MaxTokens, request.Temperature, request.TopP, request.Parameters, cancellationToken))
        {
            yield return token;
        }

        _logger.LogDebug("Text generation stream completed for model {ModelId}", modelId);
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

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;

        // Dispose the depot if it implements IDisposable
        if (_depot is IDisposable disposableDepot)
            disposableDepot.Dispose();

        _disposed = true;
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
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