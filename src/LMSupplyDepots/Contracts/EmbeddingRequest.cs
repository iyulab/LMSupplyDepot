namespace LMSupplyDepots.Contracts;

/// <summary>
/// Represents a request for embeddings generation.
/// </summary>
public class EmbeddingRequest
{
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Texts to generate embeddings for.
    /// </summary>
    public required IReadOnlyList<string> Texts { get; init; }

    /// <summary>
    /// Whether to normalize vectors to unit length.
    /// </summary>
    public bool Normalize { get; set; } = false;

    /// <summary>
    /// Additional model-specific parameters.
    /// </summary>
    public Dictionary<string, object?> Parameters { get; set; } = new();
}

/// <summary>
/// Represents a response from embeddings generation.
/// </summary>
public class EmbeddingResponse
{
    /// <summary>
    /// Generated embeddings.
    /// </summary>
    public required IReadOnlyList<float[]> Embeddings { get; init; }

    /// <summary>
    /// Dimensionality of the embeddings.
    /// </summary>
    public int Dimension => Embeddings.FirstOrDefault()?.Length ?? 0;

    /// <summary>
    /// Total tokens processed.
    /// </summary>
    public int TotalTokens { get; set; }

    /// <summary>
    /// Time taken for generation.
    /// </summary>
    public TimeSpan ElapsedTime { get; set; }
}