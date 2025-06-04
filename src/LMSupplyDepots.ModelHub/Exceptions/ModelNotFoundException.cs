namespace LMSupplyDepots.ModelHub.Exceptions;

/// <summary>
/// Exception thrown when a model is not found
/// </summary>
public class ModelNotFoundException : ModelHubException
{
    /// <summary>
    /// ID of the model that was not found
    /// </summary>
    public string ModelId { get; }

    /// <summary>
    /// Initializes a new instance of the ModelNotFoundException class
    /// </summary>
    public ModelNotFoundException(string modelId, string message)
        : base($"Model '{modelId}' not found: {message}")
    {
        ModelId = modelId;
    }

    /// <summary>
    /// Initializes a new instance of the ModelNotFoundException class with an inner exception
    /// </summary>
    public ModelNotFoundException(string modelId, string message, Exception innerException)
        : base($"Model '{modelId}' not found: {message}", innerException)
    {
        ModelId = modelId;
    }
}