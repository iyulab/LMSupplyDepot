namespace LMSupplyDepots.Exceptions;

/// <summary>
/// Exception thrown when a model fails to load.
/// </summary>
public class ModelLoadException : LMException
{
    /// <summary>
    /// ID of the model that failed to load.
    /// </summary>
    public string ModelId { get; }

    /// <summary>
    /// Initializes a new instance of the ModelLoadException class.
    /// </summary>
    public ModelLoadException(string modelId, string message)
        : base($"Failed to load model '{modelId}': {message}")
    {
        ModelId = modelId;
    }

    /// <summary>
    /// Initializes a new instance of the ModelLoadException class with an inner exception.
    /// </summary>
    public ModelLoadException(string modelId, string message, Exception innerException)
        : base($"Failed to load model '{modelId}': {message}", innerException)
    {
        ModelId = modelId;
    }
}