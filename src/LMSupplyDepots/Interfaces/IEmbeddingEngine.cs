using LMSupplyDepots.Contracts;

namespace LMSupplyDepots.Interfaces;

/// <summary>
/// Interface for embedding generation capabilities.
/// </summary>
public interface IEmbeddingEngine
{
    /// <summary>
    /// Generates embeddings for the provided texts.
    /// </summary>
    Task<EmbeddingResponse> GenerateEmbeddingsAsync(
        EmbeddingRequest request,
        CancellationToken cancellationToken = default);
}