using LMSupplyDepots.Contracts;

namespace LMSupplyDepots.Interfaces;

/// <summary>
/// Interface for text generation capabilities.
/// </summary>
public interface ITextGenerationEngine
{
    /// <summary>
    /// Generates text based on the provided request.
    /// </summary>
    Task<GenerationResponse> GenerateAsync(
        GenerationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams generated text tokens as they are produced.
    /// </summary>
    IAsyncEnumerable<string> GenerateStreamAsync(
        GenerationRequest request,
        CancellationToken cancellationToken = default);
}