namespace LMSupplyDepots.Exceptions;

/// <summary>
/// Exception thrown when text generation fails.
/// </summary>
public class GenerationException : LMException
{
    /// <summary>
    /// ID of the model that failed during generation.
    /// </summary>
    public string? ModelId { get; }

    /// <summary>
    /// Initializes a new instance of the GenerationException class.
    /// </summary>
    public GenerationException(string? modelId, string message)
        : base(modelId != null
               ? $"Text generation failed with model '{modelId}': {message}"
               : $"Text generation failed: {message}")
    {
        ModelId = modelId;
    }

    /// <summary>
    /// Initializes a new instance of the GenerationException class with an inner exception.
    /// </summary>
    public GenerationException(string? modelId, string message, Exception innerException)
        : base(modelId != null
               ? $"Text generation failed with model '{modelId}': {message}"
               : $"Text generation failed: {message}",
               innerException)
    {
        ModelId = modelId;
    }
}