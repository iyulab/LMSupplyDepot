namespace LMSupplyDepots.Exceptions;

/// <summary>
/// Base exception for all language model related exceptions.
/// </summary>
public class LMException : Exception
{
    /// <summary>
    /// Initializes a new instance of the LMException class.
    /// </summary>
    public LMException() : base() { }

    /// <summary>
    /// Initializes a new instance of the LMException class with a message.
    /// </summary>
    public LMException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the LMException class with a message and inner exception.
    /// </summary>
    public LMException(string message, Exception innerException)
        : base(message, innerException) { }
}
