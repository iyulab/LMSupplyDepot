using LMSupplyDepots.External.HuggingFace.Models;

namespace LMSupplyDepots.Host;

/// <summary>
/// Hosting service for LMSupplyDepots operations that delegates functionality to LMSupplyDepot SDK
/// </summary>
public class HostService : IHostService, IAsyncDisposable
{
    private readonly ILogger<HostService> _logger;
    private readonly LMSupplyDepot _depot;
    private bool _disposed;

    public HostService(ILogger<HostService> logger, IOptions<LMSupplyDepotOptions> options)
    {
        _logger = logger;

        var _options = options.Value;
        _depot = new LMSupplyDepot(_options, CreateLoggerFactory(logger));
        _logger.LogInformation("LMSupplyDepots Host Service initialized with models directory: {ModelsDirectory}", _options.DataPath);
    }

    private static ILoggerFactory CreateLoggerFactory(ILogger logger)
    {
        return LoggerFactory.Create(builder =>
        {
            builder.AddProvider(new ForwardingLoggerProvider(logger));
        });
    }

    #region Local Model Management

    public Task<IReadOnlyList<LMModel>> ListModelsAsync(ModelType? type = null, string? searchTerm = null, CancellationToken cancellationToken = default)
    {
        return _depot.ListModelsAsync(type, searchTerm, cancellationToken);
    }

    public Task<LMModel?> GetModelAsync(string modelId, CancellationToken cancellationToken = default)
    {
        return _depot.GetModelAsync(modelId, cancellationToken);
    }

    public Task<LMModel?> GetModelByAliasAsync(string alias, CancellationToken cancellationToken = default)
    {
        return _depot.GetModelByAliasAsync(alias, cancellationToken);
    }

    public Task<bool> IsModelDownloadedAsync(string modelId, CancellationToken cancellationToken = default)
    {
        return _depot.IsModelDownloadedAsync(modelId, cancellationToken);
    }

    public Task<bool> DeleteModelAsync(string modelId, CancellationToken cancellationToken = default)
    {
        return _depot.DeleteModelAsync(modelId, cancellationToken);
    }

    public Task<LMModel> SetModelAliasAsync(string modelId, string? alias, CancellationToken cancellationToken = default)
    {
        return _depot.SetModelAliasAsync(modelId, alias, cancellationToken);
    }

    #endregion

    #region Collection Discovery

    public Task<IReadOnlyList<LMCollection>> DiscoverCollectionsAsync(ModelType? type = null, string? searchTerm = null, int limit = 10, ModelSortField sort = ModelSortField.Downloads, CancellationToken cancellationToken = default)
    {
        return _depot.DiscoverCollectionsAsync(type, searchTerm, limit, sort, cancellationToken);
    }

    public Task<LMCollection> GetCollectionInfoAsync(string collectionId, CancellationToken cancellationToken = default)
    {
        return _depot.GetCollectionInfoAsync(collectionId, cancellationToken);
    }

    #endregion

    #region Model Download Management

    public Task<LMModel> DownloadModelAsync(string modelId, IProgress<ModelDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        return _depot.DownloadModelAsync(modelId, progress, cancellationToken);
    }

    public Task<bool> PauseDownloadAsync(string modelId, CancellationToken cancellationToken = default)
    {
        return _depot.PauseDownloadAsync(modelId, cancellationToken);
    }

    public Task<LMModel> ResumeDownloadAsync(string modelId, IProgress<ModelDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        return _depot.ResumeDownloadAsync(modelId, progress, cancellationToken);
    }

    public Task<bool> CancelDownloadAsync(string modelId, CancellationToken cancellationToken = default)
    {
        return _depot.CancelDownloadAsync(modelId, cancellationToken);
    }

    public Task<ModelDownloadStatus?> GetDownloadStatusAsync(string modelId, CancellationToken cancellationToken = default)
    {
        return _depot.GetDownloadStatusAsync(modelId, cancellationToken);
    }

    public Task<ModelDownloadProgress?> GetDownloadProgressAsync(string modelId, CancellationToken cancellationToken = default)
    {
        return _depot.GetDownloadProgressAsync(modelId, cancellationToken);
    }

    public Task<IEnumerable<DownloadInfo>> GetAllDownloadsAsync(CancellationToken cancellationToken = default)
    {
        return _depot.GetAllDownloadsAsync(cancellationToken);
    }

    #endregion

    #region Model Loading and Inference

    public Task<bool> IsModelLoadedAsync(string modelId, CancellationToken cancellationToken = default)
    {
        return _depot.IsModelLoadedAsync(modelId, cancellationToken);
    }

    public Task<IReadOnlyList<LMModel>> GetLoadedModelsAsync(CancellationToken cancellationToken = default)
    {
        return _depot.GetLoadedModelsAsync(cancellationToken);
    }

    public Task<LMModel> LoadModelAsync(string modelId, Dictionary<string, object?>? parameters = null, CancellationToken cancellationToken = default)
    {
        return _depot.LoadModelAsync(modelId, parameters, cancellationToken);
    }

    public Task UnloadModelAsync(string modelId, CancellationToken cancellationToken = default)
    {
        return _depot.UnloadModelAsync(modelId, cancellationToken);
    }

    #endregion

    #region Text Generation

    public Task<GenerationResponse> GenerateTextAsync(string modelId, GenerationRequest request, CancellationToken cancellationToken = default)
    {
        return _depot.GenerateTextAsync(modelId, request, cancellationToken);
    }

    public async IAsyncEnumerable<string> GenerateTextStreamAsync(
        string modelId,
        GenerationRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        request.Stream = true;

        await foreach (var token in _depot.GenerateTextStreamAsync(
            modelId,
            request.Prompt,
            request.MaxTokens,
            request.Temperature,
            request.TopP,
            request.Parameters,
            cancellationToken))
        {
            yield return token;
        }
    }

    public Task<GenerationResponse> GenerateTextAsync(
        string modelId,
        string prompt,
        int maxTokens = 256,
        float temperature = 0.7f,
        float topP = 0.95f,
        Dictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        return _depot.GenerateTextAsync(
            modelId,
            prompt,
            maxTokens,
            temperature,
            topP,
            parameters,
            cancellationToken);
    }

    #endregion

    #region Embeddings

    public Task<EmbeddingResponse> GenerateEmbeddingsAsync(
        string modelId,
        EmbeddingRequest request,
        CancellationToken cancellationToken = default)
    {
        return _depot.GenerateEmbeddingsAsync(modelId, request, cancellationToken);
    }

    public Task<EmbeddingResponse> GenerateEmbeddingsAsync(
        string modelId,
        IReadOnlyList<string> texts,
        bool normalize = false,
        Dictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        return _depot.GenerateEmbeddingsAsync(
            modelId,
            texts,
            normalize,
            parameters,
            cancellationToken);
    }

    #endregion

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        if (_depot is IDisposable disposableDepot)
        {
            disposableDepot.Dispose();
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

internal class ForwardingLoggerProvider : ILoggerProvider
{
    private readonly ILogger _logger;

    public ForwardingLoggerProvider(ILogger logger)
    {
        _logger = logger;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _logger;
    }

    public void Dispose()
    {
    }
}