namespace LMSupplyDepots.SDK.Services;

/// <summary>
/// Service for extracting and managing model metadata from GGUF files
/// </summary>
public interface IModelMetadataService
{
    /// <summary>
    /// Extract metadata from a loaded model
    /// </summary>
    Task<ModelMetadata> GetModelMetadataAsync(string modelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Apply the model's native chat template to format messages
    /// </summary>
    Task<string> ApplyChatTemplateAsync(
        string modelId,
        IEnumerable<ChatMessage> messages,
        bool addGenerationPrompt = true,
        ToolCallOptions? toolOptions = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a model supports tool calling
    /// </summary>
    Task<bool> SupportsToolCallingAsync(string modelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the tool calling format for a specific model
    /// </summary>
    Task<string> GetToolCallFormatAsync(string modelId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Model metadata extracted from GGUF file
/// </summary>
public class ModelMetadata
{
    public string Architecture { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string ModelType { get; set; } = string.Empty;
    public string? ChatTemplate { get; set; }
    public Dictionary<string, object> SpecialTokens { get; set; } = new();
    public ToolCapabilities ToolCapabilities { get; set; } = new();
    public int ContextLength { get; set; }
    public int VocabularySize { get; set; }
    public int EmbeddingLength { get; set; }
    public Dictionary<string, string> RawMetadata { get; set; } = new();
}

/// <summary>
/// Tool calling capabilities information
/// </summary>
public class ToolCapabilities
{
    public bool SupportsToolCalling { get; set; }
    public string ToolCallFormat { get; set; } = string.Empty;
    public Dictionary<string, string> ToolTokens { get; set; } = new();
    public string StartToken { get; set; } = string.Empty;
    public string EndToken { get; set; } = string.Empty;
}

/// <summary>
/// Chat message structure
/// </summary>
public class ChatMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

/// <summary>
/// Tool calling options
/// </summary>
public class ToolCallOptions
{
    public bool HasTools => Tools.Any();
    public IEnumerable<ToolDefinition> Tools { get; set; } = Enumerable.Empty<ToolDefinition>();
}

/// <summary>
/// Tool definition structure
/// </summary>
public class ToolDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public object Parameters { get; set; } = new();
}
