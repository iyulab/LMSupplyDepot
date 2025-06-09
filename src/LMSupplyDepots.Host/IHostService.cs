using LMSupplyDepots.External.HuggingFace.Models;

namespace LMSupplyDepots.Host;

/// <summary>
/// Interface for hosting LMSupplyDepots operations
/// </summary>
public interface IHostService
{
    #region Local Model Management

    Task<IReadOnlyList<LMModel>> ListModelsAsync(ModelType? type = null, string? searchTerm = null, CancellationToken cancellationToken = default);
    Task<LMModel?> GetModelAsync(string modelId, CancellationToken cancellationToken = default);
    Task<LMModel?> GetModelByAliasAsync(string alias, CancellationToken cancellationToken = default);
    Task<bool> IsModelDownloadedAsync(string modelId, CancellationToken cancellationToken = default);
    Task<bool> DeleteModelAsync(string modelId, CancellationToken cancellationToken = default);
    Task<LMModel> SetModelAliasAsync(string modelId, string? alias, CancellationToken cancellationToken = default);

    #endregion

    #region Collection Discovery

    Task<IReadOnlyList<LMCollection>> DiscoverCollectionsAsync(ModelType? type = null, string? searchTerm = null, int limit = 10, ModelSortField sort = ModelSortField.Downloads, CancellationToken cancellationToken = default);
    Task<LMCollection> GetCollectionInfoAsync(string collectionId, CancellationToken cancellationToken = default);

    #endregion

    #region Model Download Management

    Task<LMModel> DownloadModelAsync(string modelId, IProgress<ModelDownloadProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<bool> PauseDownloadAsync(string modelId, CancellationToken cancellationToken = default);
    Task<LMModel> ResumeDownloadAsync(string modelId, IProgress<ModelDownloadProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<bool> CancelDownloadAsync(string modelId, CancellationToken cancellationToken = default);
    Task<ModelDownloadStatus?> GetDownloadStatusAsync(string modelId, CancellationToken cancellationToken = default);
    Task<ModelDownloadProgress?> GetDownloadProgressAsync(string modelId, CancellationToken cancellationToken = default);
    Task<IEnumerable<DownloadInfo>> GetAllDownloadsAsync(CancellationToken cancellationToken = default);

    #endregion

    #region Model Loading and Inference

    Task<bool> IsModelLoadedAsync(string modelId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LMModel>> GetLoadedModelsAsync(CancellationToken cancellationToken = default);
    Task<LMModel> LoadModelAsync(string modelId, Dictionary<string, object?>? parameters = null, CancellationToken cancellationToken = default);
    Task UnloadModelAsync(string modelId, CancellationToken cancellationToken = default);

    #endregion

    #region Text Generation

    Task<GenerationResponse> GenerateTextAsync(string modelId, GenerationRequest request, CancellationToken cancellationToken = default);
    IAsyncEnumerable<string> GenerateTextStreamAsync(string modelId, GenerationRequest request, CancellationToken cancellationToken = default);
    Task<GenerationResponse> GenerateTextAsync(string modelId, string prompt, int maxTokens = 256, float temperature = 0.7f, float topP = 0.95f, Dictionary<string, object?>? parameters = null, CancellationToken cancellationToken = default);

    #endregion

    #region Embeddings

    Task<EmbeddingResponse> GenerateEmbeddingsAsync(string modelId, EmbeddingRequest request, CancellationToken cancellationToken = default);
    Task<EmbeddingResponse> GenerateEmbeddingsAsync(string modelId, IReadOnlyList<string> texts, bool normalize = false, Dictionary<string, object?>? parameters = null, CancellationToken cancellationToken = default);

    #endregion
}