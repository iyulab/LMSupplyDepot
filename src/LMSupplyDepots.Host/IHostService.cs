using LMSupplyDepots.External.HuggingFace.Models;
using LMSupplyDepots.Models;

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

    /// <summary>
    /// Gets download progress for a model
    /// </summary>
    Task<ModelDownloadProgress?> GetDownloadProgressAsync(string modelKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets information about all current downloads
    /// </summary>
    Task<IEnumerable<DownloadInfo>> GetAllDownloadsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a download with validation and returns operation result
    /// </summary>
    Task<DownloadOperationResult> StartDownloadAsync(string modelKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pauses a download with validation and returns operation result
    /// </summary>
    Task<DownloadOperationResult> PauseDownloadAsync(string modelKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes a download with validation and returns operation result
    /// </summary>
    Task<DownloadOperationResult> ResumeDownloadAsync(string modelKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a download with validation and returns operation result
    /// </summary>
    Task<DownloadOperationResult> CancelDownloadAsync(string modelKey, CancellationToken cancellationToken = default);

    #endregion

    #region Model Loading and Inference

    Task<bool> IsModelLoadedAsync(string modelId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LMModel>> GetLoadedModelsAsync(CancellationToken cancellationToken = default);
    Task<LMModel> LoadModelAsync(string modelId, Dictionary<string, object?>? parameters = null, CancellationToken cancellationToken = default);
    Task UnloadModelAsync(string modelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the runtime state of a model
    /// </summary>
    Task<ModelRuntimeState> GetModelRuntimeStateAsync(string modelKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets runtime states for all models
    /// </summary>
    IReadOnlyDictionary<string, ModelRuntimeState> GetAllModelRuntimeStates();

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

    #region OpenAI Compatibility

    Task<object> ListModelsOpenAIAsync(CancellationToken cancellationToken = default);
    Task<object> CreateChatCompletionAsync(object request, CancellationToken cancellationToken = default);
    Task<object> CreateEmbeddingsAsync(object request, CancellationToken cancellationToken = default);
    IAsyncEnumerable<string> CreateChatCompletionStreamAsync(object request, CancellationToken cancellationToken = default);

    #endregion
}

/// <summary>
/// Result for download operations with validation
/// </summary>
public class DownloadOperationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public ModelDownloadProgress? Progress { get; set; }

    public static DownloadOperationResult CreateSuccess(string message, string status, ModelDownloadProgress? progress = null)
    {
        return new DownloadOperationResult
        {
            Success = true,
            Message = message,
            Status = status,
            Progress = progress
        };
    }

    public static DownloadOperationResult CreateFailure(string message, string status = "Failed")
    {
        return new DownloadOperationResult
        {
            Success = false,
            Message = message,
            Status = status
        };
    }
}