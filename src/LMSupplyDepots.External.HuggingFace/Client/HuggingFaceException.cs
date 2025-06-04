using System.Net;

namespace LMSupplyDepots.External.HuggingFace.Client;

/// <summary>
/// Represents errors that occur during Hugging Face API operations.
/// </summary>
public class HuggingFaceException : Exception
{
    /// <summary>
    /// Gets the HTTP status code associated with the error, if applicable.
    /// </summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>
    /// Initializes a new instance of the HuggingFaceException class.
    /// </summary>
    public HuggingFaceException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the HuggingFaceException class with a status code.
    /// </summary>
    public HuggingFaceException(string message, HttpStatusCode statusCode)
        : base(message)
    {
        StatusCode = statusCode;
    }

    /// <summary>
    /// Initializes a new instance of the HuggingFaceException class with an inner exception.
    /// </summary>
    public HuggingFaceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the HuggingFaceException class with a status code and inner exception.
    /// </summary>
    public HuggingFaceException(string message, HttpStatusCode statusCode, Exception innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}