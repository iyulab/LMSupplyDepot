using System.Text.Json.Serialization;

namespace LMSupplyDepots.Host.Models.OpenAI;

/// <summary>
/// OpenAI-compatible models list response
/// </summary>
public class OpenAIModelsResponse
{
    [JsonPropertyName("object")]
    public string Object { get; set; } = "list";

    [JsonPropertyName("data")]
    public List<OpenAIModel> Data { get; set; } = new();
}

/// <summary>
/// OpenAI-compatible model object
/// </summary>
public class OpenAIModel
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("object")]
    public string Object { get; set; } = "model";

    [JsonPropertyName("created")]
    public long Created { get; set; }

    [JsonPropertyName("owned_by")]
    public string OwnedBy { get; set; } = "local";

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
}

/// <summary>
/// OpenAI-compatible chat completion request
/// </summary>
public class OpenAIChatCompletionRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<OpenAIChatMessage> Messages { get; set; } = new();

    [JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; set; }

    [JsonPropertyName("temperature")]
    public float? Temperature { get; set; }

    [JsonPropertyName("top_p")]
    public float? TopP { get; set; }

    [JsonPropertyName("stream")]
    public bool Stream { get; set; } = false;

    [JsonPropertyName("stop")]
    public List<string>? Stop { get; set; }

    [JsonPropertyName("presence_penalty")]
    public float? PresencePenalty { get; set; }

    [JsonPropertyName("frequency_penalty")]
    public float? FrequencyPenalty { get; set; }
}

/// <summary>
/// OpenAI-compatible chat message
/// </summary>
public class OpenAIChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

/// <summary>
/// OpenAI-compatible chat completion response
/// </summary>
public class OpenAIChatCompletionResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("object")]
    public string Object { get; set; } = "chat.completion";

    [JsonPropertyName("created")]
    public long Created { get; set; }

    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("choices")]
    public List<OpenAIChatChoice> Choices { get; set; } = new();

    [JsonPropertyName("usage")]
    public OpenAIUsage Usage { get; set; } = new();
}

/// <summary>
/// OpenAI-compatible chat choice
/// </summary>
public class OpenAIChatChoice
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("message")]
    public OpenAIChatMessage Message { get; set; } = new();

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

/// <summary>
/// OpenAI-compatible embeddings request
/// </summary>
public class OpenAIEmbeddingRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("input")]
    public object Input { get; set; } = string.Empty;

    [JsonPropertyName("encoding_format")]
    public string? EncodingFormat { get; set; } = "float";

    [JsonPropertyName("dimensions")]
    public int? Dimensions { get; set; }

    [JsonPropertyName("user")]
    public string? User { get; set; }
}

/// <summary>
/// OpenAI-compatible embeddings response
/// </summary>
public class OpenAIEmbeddingResponse
{
    [JsonPropertyName("object")]
    public string Object { get; set; } = "list";

    [JsonPropertyName("data")]
    public List<OpenAIEmbeddingData> Data { get; set; } = new();

    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("usage")]
    public OpenAIUsage Usage { get; set; } = new();
}

/// <summary>
/// OpenAI-compatible embedding data
/// </summary>
public class OpenAIEmbeddingData
{
    [JsonPropertyName("object")]
    public string Object { get; set; } = "embedding";

    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("embedding")]
    public float[] Embedding { get; set; } = Array.Empty<float>();
}

/// <summary>
/// OpenAI-compatible usage information
/// </summary>
public class OpenAIUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int? CompletionTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }
}

/// <summary>
/// OpenAI-compatible error response
/// </summary>
public class OpenAIErrorResponse
{
    [JsonPropertyName("error")]
    public OpenAIError Error { get; set; } = new();
}

/// <summary>
/// OpenAI-compatible error object
/// </summary>
public class OpenAIError
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("param")]
    public string? Param { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }
}
