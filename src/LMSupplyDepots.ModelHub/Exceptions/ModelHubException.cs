namespace LMSupplyDepots.ModelHub.Exceptions;

/// <summary>
/// Base exception for all model hub related exceptions
/// </summary>
public class ModelHubException : Exception
{
    /// <summary>
    /// Initializes a new instance of the ModelHubException class
    /// </summary>
    public ModelHubException() : base() { }

    /// <summary>
    /// Initializes a new instance of the ModelHubException class with a message
    /// </summary>
    public ModelHubException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the ModelHubException class with a message and inner exception
    /// </summary>
    public ModelHubException(string message, Exception innerException)
        : base(message, innerException) { }
}