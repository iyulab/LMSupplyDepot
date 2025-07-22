using System.Text.Json.Serialization;
using LMSupplyDepots.Host.Converters;

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
    public string Model { get; set; } = string.Empty; // required

    [JsonPropertyName("messages")]
    public List<OpenAIChatMessage> Messages { get; set; } = new(); // required

    [JsonPropertyName("audio")]
    public AudioOutputRequest? Audio { get; set; } // optional

    [JsonPropertyName("frequency_penalty")]
    public float? FrequencyPenalty { get; set; } // optional

    [JsonPropertyName("logit_bias")]
    public Dictionary<string, int>? LogitBias { get; set; } // optional

    [JsonPropertyName("logprobs")]
    public bool? Logprobs { get; set; } // optional

    [JsonPropertyName("max_completion_tokens")]
    public int? MaxCompletionTokens { get; set; } // optional

    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; set; } // optional

    [JsonPropertyName("modalities")]
    public List<string>? Modalities { get; set; } // optional

    [JsonPropertyName("n")]
    public int? N { get; set; } // optional

    [JsonPropertyName("parallel_tool_calls")]
    public bool? ParallelToolCalls { get; set; } // optional

    [JsonPropertyName("prediction")]
    public PredictionRequest? Prediction { get; set; } // optional

    [JsonPropertyName("presence_penalty")]
    public float? PresencePenalty { get; set; } // optional

    [JsonPropertyName("reasoning_effort")]
    public string? ReasoningEffort { get; set; } // optional

    [JsonPropertyName("response_format")]
    public ResponseFormat? ResponseFormat { get; set; } // optional

    [JsonPropertyName("seed")]
    public int? Seed { get; set; } // optional

    [JsonPropertyName("service_tier")]
    public string? ServiceTier { get; set; } // optional

    [JsonPropertyName("stop")]
    [JsonConverter(typeof(StopSequenceConverter))]
    public StopSequence? Stop { get; set; } // optional

    [JsonPropertyName("store")]
    public bool? Store { get; set; } // optional

    [JsonPropertyName("stream")]
    public bool? Stream { get; set; } // optional

    [JsonPropertyName("stream_options")]
    public StreamOptions? StreamOptions { get; set; } // optional

    [JsonPropertyName("temperature")]
    public float? Temperature { get; set; } // optional

    [JsonPropertyName("tool_choice")]
    public ToolChoice? ToolChoice { get; set; } // optional

    [JsonPropertyName("tools")]
    public List<Tool>? Tools { get; set; } // optional

    [JsonPropertyName("top_logprobs")]
    public int? TopLogprobs { get; set; } // optional

    [JsonPropertyName("top_p")]
    public float? TopP { get; set; } // optional

    [JsonPropertyName("user")]
    public string? User { get; set; } // optional

    [JsonPropertyName("web_search_options")]
    public WebSearchOptions? WebSearchOptions { get; set; } // optional
}

/// <summary>
/// OpenAI-compatible chat message
/// </summary>
public class OpenAIChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty; // required

    [JsonPropertyName("content")]
    [JsonConverter(typeof(ContentPartConverter))]
    public ContentPart? Content { get; set; } // required for most roles, optional for assistant with tool_calls

    [JsonPropertyName("name")]
    public string? Name { get; set; } // optional

    [JsonPropertyName("tool_call_id")]
    public string? ToolCallId { get; set; } // required for tool messages

    [JsonPropertyName("audio")]
    public AudioResponse? Audio { get; set; } // optional for assistant

    [JsonPropertyName("tool_calls")]
    public List<ToolCall>? ToolCalls { get; set; } // optional for assistant

    [JsonPropertyName("refusal")]
    public string? Refusal { get; set; } // optional for assistant
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

/// <summary>
/// Audio output request parameters
/// </summary>
public class AudioOutputRequest
{
    [JsonPropertyName("format")]
    public string Format { get; set; } = string.Empty; // required

    [JsonPropertyName("voice")]
    public string Voice { get; set; } = string.Empty; // required
}

/// <summary>
/// Prediction request for predicted outputs
/// </summary>
public class PredictionRequest
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "content"; // required

    [JsonPropertyName("content")]
    public ContentPart Content { get; set; } = null!; // required
}

/// <summary>
/// Response format specification
/// </summary>
public class ResponseFormat
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty; // required

    [JsonPropertyName("json_schema")]
    public JsonSchema? JsonSchema { get; set; } // optional
}

/// <summary>
/// JSON schema for structured outputs
/// </summary>
public class JsonSchema
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty; // required

    [JsonPropertyName("description")]
    public string? Description { get; set; } // optional

    [JsonPropertyName("schema")]
    public Dictionary<string, object>? Schema { get; set; } // optional

    [JsonPropertyName("strict")]
    public bool? Strict { get; set; } // optional
}

/// <summary>
/// Stop sequence specification
/// </summary>
public class StopSequence
{
    public static implicit operator StopSequence(string value) => new() { Single = value };
    public static implicit operator StopSequence(List<string> values) => new() { Multiple = values };

    [JsonIgnore]
    public string? Single { get; set; }

    [JsonIgnore]
    public List<string>? Multiple { get; set; }

    public object? ToJson() => Single ?? (object?)Multiple;
}

/// <summary>
/// Stream options for streaming responses
/// </summary>
public class StreamOptions
{
    [JsonPropertyName("include_usage")]
    public bool? IncludeUsage { get; set; } // optional
}

/// <summary>
/// Tool choice specification
/// </summary>
public class ToolChoice
{
    public static implicit operator ToolChoice(string value) => new() { Type = value };

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("function")]
    public FunctionChoice? Function { get; set; }
}

/// <summary>
/// Function choice specification
/// </summary>
public class FunctionChoice
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty; // required
}

/// <summary>
/// Tool definition
/// </summary>
public class Tool
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function"; // required

    [JsonPropertyName("function")]
    public FunctionDefinition Function { get; set; } = null!; // required
}

/// <summary>
/// Function definition
/// </summary>
public class FunctionDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty; // required

    [JsonPropertyName("description")]
    public string? Description { get; set; } // optional

    [JsonPropertyName("parameters")]
    public Dictionary<string, object>? Parameters { get; set; } // optional

    [JsonPropertyName("strict")]
    public bool? Strict { get; set; } // optional
}

/// <summary>
/// Web search options
/// </summary>
public class WebSearchOptions
{
    [JsonPropertyName("search_context_size")]
    public string? SearchContextSize { get; set; } // optional

    [JsonPropertyName("user_location")]
    public UserLocation? UserLocation { get; set; } // optional
}

/// <summary>
/// User location for web search
/// </summary>
public class UserLocation
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "approximate"; // required

    [JsonPropertyName("approximate")]
    public ApproximateLocation Approximate { get; set; } = null!; // required
}

/// <summary>
/// Approximate location parameters
/// </summary>
public class ApproximateLocation
{
    [JsonPropertyName("city")]
    public string? City { get; set; } // optional

    [JsonPropertyName("country")]
    public string? Country { get; set; } // optional

    [JsonPropertyName("region")]
    public string? Region { get; set; } // optional

    [JsonPropertyName("timezone")]
    public string? Timezone { get; set; } // optional
}

/// <summary>
/// Content part (can be text, image, audio, file, etc.)
/// </summary>
public class ContentPart
{
    public static implicit operator ContentPart(string text) => new TextContentPart { Text = text };

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty; // required
}

/// <summary>
/// Text content part
/// </summary>
public class TextContentPart : ContentPart
{
    public TextContentPart() { Type = "text"; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty; // required
}

/// <summary>
/// Image content part
/// </summary>
public class ImageContentPart : ContentPart
{
    public ImageContentPart() { Type = "image_url"; }

    [JsonPropertyName("image_url")]
    public ImageUrl ImageUrl { get; set; } = null!; // required
}

/// <summary>
/// Image URL specification
/// </summary>
public class ImageUrl
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty; // required

    [JsonPropertyName("detail")]
    public string? Detail { get; set; } // optional
}

/// <summary>
/// Audio response information
/// </summary>
public class AudioResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty; // required
}

/// <summary>
/// Tool call made by the model
/// </summary>
public class ToolCall
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty; // required

    [JsonPropertyName("type")]
    public string Type { get; set; } = "function"; // required

    [JsonPropertyName("function")]
    public FunctionCall Function { get; set; } = null!; // required
}

/// <summary>
/// Function call details
/// </summary>
public class FunctionCall
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty; // required

    [JsonPropertyName("arguments")]
    public string Arguments { get; set; } = string.Empty; // required
}
