using System.Text.Json.Serialization;

namespace LMSupplyDepots.Contracts;

/// <summary>
/// Response model for v1/models endpoint
/// </summary>
public class ModelsListResponse
{
    [JsonPropertyName("models")]
    public List<ModelListItem> Models { get; set; } = new List<ModelListItem>();
}

/// <summary>
/// Individual model information for v1/models response
/// </summary>
public class ModelListItem
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}