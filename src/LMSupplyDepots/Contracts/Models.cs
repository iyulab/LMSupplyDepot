using System.Text.Json.Serialization;

namespace LMSupplyDepots.Contracts;

/// <summary>
/// Request with just a model name
/// </summary>
public class ModelNameRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

public class AliasRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("alias")]
    public string Alias { get; set; } = "";
}

/// <summary>
/// Simple status response
/// </summary>
public class StatusResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";
}

/// <summary>
/// Response from a model search
/// </summary>
public class SearchResponse
{
    [JsonPropertyName("models")]
    public List<SearchModelInfo> Models { get; set; } = new List<SearchModelInfo>();
}

/// <summary>
/// Information about a model from search results
/// </summary>
public class SearchModelInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("format")]
    public string Format { get; set; } = "";

    [JsonPropertyName("is_downloaded")]
    public bool IsDownloaded { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = "";
}

/// <summary>
/// Error response (common for all endpoints)
/// </summary>
public class ErrorResponse
{
    [JsonPropertyName("error")]
    public string Error { get; set; } = "";
}

/// <summary>
/// Request for loading a model
/// </summary>
public class ModelLoadRequest
{
    /// <summary>
    /// ID of the model to load
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Optional parameters for model loading
    /// </summary>
    public Dictionary<string, object?>? Parameters { get; set; }
}

/// <summary>
/// Request for unloading a model
/// </summary>
public class ModelUnloadRequest
{
    /// <summary>
    /// ID of the model to unload
    /// </summary>
    public string Model { get; set; } = string.Empty;
}