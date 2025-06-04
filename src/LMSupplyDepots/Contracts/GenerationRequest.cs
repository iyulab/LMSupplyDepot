namespace LMSupplyDepots.Contracts;

/// <summary>
/// Represents a request for text generation.
/// </summary>
public class GenerationRequest
{
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// The input prompt for text generation.
    /// </summary>
    public string Prompt { get; set; } = string.Empty;

    /// <summary>
    /// Maximum number of tokens to generate.
    /// </summary>
    public int MaxTokens { get; set; } = 2048;

    /// <summary>
    /// Temperature for controlling randomness (0.0-2.0).
    /// </summary>
    public float Temperature { get; set; } = 0.8f;

    /// <summary>
    /// Top-p sampling value (0.0-1.0).
    /// </summary>
    public float TopP { get; set; } = 0.95f;

    /// <summary>
    /// Whether to stream tokens as they are generated.
    /// </summary>
    public bool Stream { get; set; } = false;

    /// <summary>
    /// Additional model-specific parameters.
    /// </summary>
    public Dictionary<string, object?> Parameters { get; set; } = [];
}

/// <summary>
/// Represents a response from text generation.
/// </summary>
public class GenerationResponse
{
    /// <summary>
    /// The generated text.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Reason why generation stopped.
    /// </summary>
    public string FinishReason { get; set; } = string.Empty;

    /// <summary>
    /// Number of tokens in the input prompt.
    /// </summary>
    public int PromptTokens { get; set; }

    /// <summary>
    /// Number of tokens in the generated output.
    /// </summary>
    public int OutputTokens { get; set; }

    /// <summary>
    /// Time taken for generation.
    /// </summary>
    public TimeSpan ElapsedTime { get; set; }
}