namespace LMSupplyDepots.ModelHub.Exceptions;

/// <summary>
/// Exception thrown when a model source is not found
/// </summary>
public class ModelSourceNotFoundException : ModelHubException
{
    /// <summary>
    /// Gets the source ID that was not found
    /// </summary>
    public string SourceId { get; }

    /// <summary>
    /// Initializes a new instance of the ModelSourceNotFoundException class
    /// </summary>
    public ModelSourceNotFoundException(string sourceId)
        : base($"Model source not found: '{sourceId}'")
    {
        SourceId = sourceId;
    }

    /// <summary>
    /// Initializes a new instance of the ModelSourceNotFoundException class with an inner exception
    /// </summary>
    public ModelSourceNotFoundException(string sourceId, string message)
        : base($"Model source not found: '{sourceId}': {message}")
    {
        SourceId = sourceId;
    }

    /// <summary>
    /// Initializes a new instance of the ModelSourceNotFoundException class with an inner exception
    /// </summary>
    public ModelSourceNotFoundException(string sourceId, string message, Exception innerException)
        : base($"Model source not found: '{sourceId}': {message}", innerException)
    {
        SourceId = sourceId;
    }
}