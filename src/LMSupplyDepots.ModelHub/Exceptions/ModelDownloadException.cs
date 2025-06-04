using LMSupplyDepots.External.HuggingFace.Client;

namespace LMSupplyDepots.ModelHub.Exceptions;

/// <summary>
/// Exception thrown when a model download fails
/// </summary>
public class ModelDownloadException : ModelHubException
{
    /// <summary>
    /// Gets the source ID of the model that failed to download
    /// </summary>
    public string SourceId { get; }

    /// <summary>
    /// Gets whether this is an authentication-related exception
    /// </summary>
    public bool IsAuthenticationError => InnerException is HuggingFaceException hfEx &&
                                       hfEx.StatusCode == System.Net.HttpStatusCode.Unauthorized;

    /// <summary>
    /// Initializes a new instance of the ModelDownloadException class
    /// </summary>
    public ModelDownloadException(string sourceId, string message)
        : base($"Failed to download model '{sourceId}': {message}")
    {
        SourceId = sourceId;
    }

    /// <summary>
    /// Initializes a new instance of the ModelDownloadException class with an inner exception
    /// </summary>
    public ModelDownloadException(string sourceId, string message, Exception innerException)
        : base($"Failed to download model '{sourceId}': {message}", innerException)
    {
        SourceId = sourceId;
    }

    /// <summary>
    /// Gets a user-friendly message for this exception
    /// </summary>
    public string GetUserFriendlyMessage()
    {
        if (IsAuthenticationError)
        {
            return "This model requires authentication. Please provide a valid API token in the settings.";
        }

        return Message;
    }
}