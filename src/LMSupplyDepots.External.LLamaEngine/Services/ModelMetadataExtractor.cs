using LLama;
using LLama.Native;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace LMSupplyDepots.External.LLamaEngine.Services;

/// <summary>
/// Service for extracting metadata and capabilities from GGUF model files
/// </summary>
public class ModelMetadataExtractor
{
    private readonly ILogger<ModelMetadataExtractor> _logger;

    public ModelMetadataExtractor(ILogger<ModelMetadataExtractor> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Extract comprehensive metadata from a loaded model
    /// </summary>
    public ModelMetadata ExtractMetadata(SafeLlamaModelHandle modelHandle)
    {
        try
        {
            _logger.LogDebug("Extracting metadata from model");

            // Read all metadata from the model
            var rawMetadata = modelHandle.ReadMetadata();

            var metadata = new ModelMetadata
            {
                RawMetadata = rawMetadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),

                // Extract architecture information
                Architecture = rawMetadata.TryGetValue("general.architecture", out var arch) ? arch : "unknown",
                ModelName = rawMetadata.TryGetValue("general.name", out var name) ? name : "unknown",
                ModelType = rawMetadata.TryGetValue("general.type", out var type) ? type : "unknown",

                // Extract chat template
                ChatTemplate = ExtractChatTemplate(rawMetadata),

                // Extract special tokens
                SpecialTokens = ExtractSpecialTokens(modelHandle, rawMetadata),

                // Extract tool calling capabilities
                ToolCapabilities = ExtractToolCapabilities(rawMetadata),

                // Extract model parameters
                ContextLength = ExtractContextLength(rawMetadata),
                VocabularySize = ExtractVocabularySize(rawMetadata),
                EmbeddingLength = ExtractEmbeddingLength(rawMetadata)
            };

            _logger.LogInformation("Successfully extracted metadata for {Architecture} model: {ModelName}",
                metadata.Architecture, metadata.ModelName);

            return metadata;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract model metadata");
            throw;
        }
    }

    /// <summary>
    /// Extract chat template from metadata
    /// </summary>
    private string? ExtractChatTemplate(IReadOnlyDictionary<string, string> metadata)
    {
        if (metadata.TryGetValue("tokenizer.chat_template", out var template))
        {
            _logger.LogDebug("Found chat template: {Template}", template.Substring(0, Math.Min(100, template.Length)) + "...");
            return template;
        }

        _logger.LogWarning("No chat template found in model metadata");
        return null;
    }

    /// <summary>
    /// Extract special tokens information
    /// </summary>
    private Dictionary<string, LLamaToken> ExtractSpecialTokens(SafeLlamaModelHandle modelHandle, IReadOnlyDictionary<string, string> metadata)
    {
        var specialTokens = new Dictionary<string, LLamaToken>();

        try
        {
            // Get standard special tokens
            var bosToken = NativeApi.llama_token_bos(modelHandle);
            var eosToken = NativeApi.llama_token_eos(modelHandle);
            var nlToken = NativeApi.llama_token_nl(modelHandle);

            specialTokens["BOS"] = bosToken;
            specialTokens["EOS"] = eosToken;
            specialTokens["NL"] = nlToken;

            // Extract token IDs from metadata
            if (metadata.TryGetValue("tokenizer.ggml.bos_token_id", out var bosIdStr) && int.TryParse(bosIdStr, out var bosId))
                specialTokens["BOS_ID"] = new LLamaToken(bosId);

            if (metadata.TryGetValue("tokenizer.ggml.eos_token_id", out var eosIdStr) && int.TryParse(eosIdStr, out var eosId))
                specialTokens["EOS_ID"] = new LLamaToken(eosId);

            if (metadata.TryGetValue("tokenizer.ggml.unknown_token_id", out var unkIdStr) && int.TryParse(unkIdStr, out var unkId))
                specialTokens["UNK"] = new LLamaToken(unkId);

            if (metadata.TryGetValue("tokenizer.ggml.padding_token_id", out var padIdStr) && int.TryParse(padIdStr, out var padId))
                specialTokens["PAD"] = new LLamaToken(padId);

            _logger.LogDebug("Extracted {Count} special tokens", specialTokens.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract some special tokens");
        }

        return specialTokens;
    }

    /// <summary>
    /// Extract tool calling capabilities
    /// </summary>
    private ToolCapabilities ExtractToolCapabilities(IReadOnlyDictionary<string, string> metadata)
    {
        var capabilities = new ToolCapabilities();

        // Check for tool-related tokens in the vocabulary
        var hasToolTokens = metadata.Keys.Any(key =>
            key.Contains("tool") &&
            (key.Contains("token") || key.Contains("id")));

        if (hasToolTokens)
        {
            capabilities.SupportsToolCalling = true;

            // Dynamically determine tool calling format based on architecture and model specifics
            if (metadata.TryGetValue("general.architecture", out var arch))
            {
                var architecture = arch.ToLowerInvariant();

                // Use architecture name directly for more flexible matching
                capabilities.ToolCallFormat = architecture switch
                {
                    "phi3" => DetectPhiVariant(metadata),
                    "llama" => "llama-native",
                    "mixtral" => "mixtral",
                    "qwen" => "qwen",
                    "gemma" => "gemma",
                    _ => architecture // Use architecture name as format for unknown types
                };
            }
            else
            {
                capabilities.ToolCallFormat = "generic";
            }

            // Extract tool-specific tokens
            var toolTokens = metadata
                .Where(kvp => kvp.Key.Contains("tool") && kvp.Key.Contains("token"))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            capabilities.ToolTokens = toolTokens;

            _logger.LogInformation("Model supports tool calling with format: {Format}", capabilities.ToolCallFormat);
        }
        else
        {
            _logger.LogInformation("Model does not appear to support tool calling");
        }

        return capabilities;
    }

    /// <summary>
    /// Detect specific Phi model variant based on metadata
    /// </summary>
    private string DetectPhiVariant(IReadOnlyDictionary<string, string> metadata)
    {
        // Check model name for specific variants
        if (metadata.TryGetValue("general.name", out var modelName))
        {
            var name = modelName.ToLowerInvariant();
            if (name.Contains("phi-4") || name.Contains("phi4"))
            {
                return "phi4";
            }
            if (name.Contains("phi-3.5") || name.Contains("phi3.5"))
            {
                return "phi3.5";
            }
            if (name.Contains("phi-3") || name.Contains("phi3"))
            {
                return "phi3";
            }
        }

        // Default to phi3 format for unknown Phi variants
        return "phi3";
    }

    /// <summary>
    /// Extract context length
    /// </summary>
    private int ExtractContextLength(IReadOnlyDictionary<string, string> metadata)
    {
        var keys = new[] { "llama.context_length", "phi3.context_length", "general.context_length" };

        foreach (var key in keys)
        {
            if (metadata.TryGetValue(key, out var value) && int.TryParse(value, out var contextLength))
            {
                return contextLength;
            }
        }

        _logger.LogWarning("Could not determine context length from metadata");
        return 2048; // Default fallback
    }

    /// <summary>
    /// Extract vocabulary size
    /// </summary>
    private int ExtractVocabularySize(IReadOnlyDictionary<string, string> metadata)
    {
        if (metadata.TryGetValue("tokenizer.ggml.tokens", out var tokensValue))
        {
            // The tokens value is usually in format "arr[str,COUNT]"
            var match = System.Text.RegularExpressions.Regex.Match(tokensValue, @"arr\[str,(\d+)\]");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var vocabSize))
            {
                return vocabSize;
            }
        }

        _logger.LogWarning("Could not determine vocabulary size from metadata");
        return 32000; // Default fallback
    }

    /// <summary>
    /// Extract embedding length
    /// </summary>
    private int ExtractEmbeddingLength(IReadOnlyDictionary<string, string> metadata)
    {
        var keys = new[] { "llama.embedding_length", "phi3.embedding_length", "general.embedding_length" };

        foreach (var key in keys)
        {
            if (metadata.TryGetValue(key, out var value) && int.TryParse(value, out var embeddingLength))
            {
                return embeddingLength;
            }
        }

        return 0; // No embeddings supported
    }
}

/// <summary>
/// Comprehensive model metadata extracted from GGUF file
/// </summary>
public class ModelMetadata
{
    public string Architecture { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string ModelType { get; set; } = string.Empty;
    public string? ChatTemplate { get; set; }
    public Dictionary<string, LLamaToken> SpecialTokens { get; set; } = new();
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
}
