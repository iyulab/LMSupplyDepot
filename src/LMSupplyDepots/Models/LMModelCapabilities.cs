namespace LMSupplyDepots.Models;

/// <summary>
/// Describes the core capabilities of a language model.
/// </summary>
public class LMModelCapabilities
{
    /// <summary>
    /// Whether the model supports text generation.
    /// </summary>
    public bool SupportsTextGeneration { get; set; }

    /// <summary>
    /// Whether the model supports embeddings.
    /// </summary>
    public bool SupportsEmbeddings { get; set; }

    /// <summary>
    /// Whether the model supports image understanding.
    /// </summary>
    public bool SupportsImageUnderstanding { get; set; }

    /// <summary>
    /// Maximum context length for the model.
    /// </summary>
    public int MaxContextLength { get; set; }

    /// <summary>
    /// Dimension of embeddings if the model supports them.
    /// </summary>
    public int? EmbeddingDimension { get; set; }

    /// <summary>
    /// Creates a deep copy of the capabilities
    /// </summary>
    public LMModelCapabilities Clone()
    {
        return new LMModelCapabilities
        {
            SupportsTextGeneration = this.SupportsTextGeneration,
            SupportsEmbeddings = this.SupportsEmbeddings,
            SupportsImageUnderstanding = this.SupportsImageUnderstanding,
            MaxContextLength = this.MaxContextLength,
            EmbeddingDimension = this.EmbeddingDimension
        };
    }
}