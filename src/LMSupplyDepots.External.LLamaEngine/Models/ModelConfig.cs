using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace LMSupplyDepots.External.LLamaEngine.Models;

/// <summary>
/// Enhanced model configuration with extended parameters
/// </summary>
public class ModelConfig
{
    [JsonPropertyName("total")]
    public long TotalSize { get; set; }

    [JsonPropertyName("architecture")]
    public string Architecture { get; set; } = "llama";

    [JsonPropertyName("context_length")]
    public uint ContextLength { get; set; } = 2048;

    [JsonPropertyName("chat_template")]
    public string? ChatTemplate { get; set; }

    [JsonPropertyName("bos_token")]
    public string BosToken { get; set; } = "<s>";

    [JsonPropertyName("eos_token")]
    public string EosToken { get; set; } = "</s>";

    [JsonPropertyName("pad_token")]
    public string? PadToken { get; set; }

    [JsonPropertyName("unk_token")]
    public string? UnkToken { get; set; }

    // Tokenizer settings
    [JsonPropertyName("vocab_size")]
    public int? VocabSize { get; set; }

    [JsonPropertyName("max_position_embeddings")]
    public int? MaxPositionEmbeddings { get; set; }

    // Model-specific parameters
    [JsonPropertyName("hidden_size")]
    public int? HiddenSize { get; set; }

    [JsonPropertyName("intermediate_size")]
    public int? IntermediateSize { get; set; }

    [JsonPropertyName("num_attention_heads")]
    public int? NumAttentionHeads { get; set; }

    [JsonPropertyName("num_hidden_layers")]
    public int? NumHiddenLayers { get; set; }

    [JsonPropertyName("num_key_value_heads")]
    public int? NumKeyValueHeads { get; set; }

    [JsonPropertyName("rope_theta")]
    public float? RopeTheta { get; set; }

    [JsonPropertyName("rope_scaling")]
    public RopeScaling? RopeScaling { get; set; }

    // Inference optimization settings
    [JsonPropertyName("preferred_batch_size")]
    public uint? PreferredBatchSize { get; set; }

    [JsonPropertyName("preferred_gpu_layers")]
    public int? PreferredGpuLayers { get; set; }

    [JsonPropertyName("preferred_threads")]
    public int? PreferredThreads { get; set; }

    [JsonPropertyName("flash_attention")]
    public bool? FlashAttention { get; set; }

    [JsonPropertyName("use_mmap")]
    public bool? UseMmap { get; set; } = true;

    [JsonPropertyName("use_mlock")]
    public bool? UseMlock { get; set; } = false;

    // Model family and template information
    [JsonPropertyName("model_family")]
    public string? ModelFamily { get; set; }

    [JsonPropertyName("template_type")]
    public string? TemplateType { get; set; }

    [JsonPropertyName("supports_tools")]
    public bool? SupportsTools { get; set; }

    [JsonPropertyName("supports_system_message")]
    public bool? SupportsSystemMessage { get; set; } = true;

    // Performance and compatibility
    [JsonPropertyName("recommended_max_tokens")]
    public int? RecommendedMaxTokens { get; set; }

    [JsonPropertyName("warning_max_tokens")]
    public int? WarningMaxTokens { get; set; }

    public static ModelConfig Default => new()
    {
        Architecture = "llama",
        ContextLength = 2048,
        BosToken = "<s>",
        EosToken = "</s>",
        UseMmap = true,
        UseMlock = false,
        SupportsSystemMessage = true
    };

    public static ModelConfig LoadFromFile(string configPath, ILogger logger)
    {
        try
        {
            if (!File.Exists(configPath))
            {
                logger.LogWarning("Configuration file not found at {ConfigPath}, using default configuration", configPath);
                return Default;
            }

            var jsonString = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<ModelConfig>(jsonString, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            if (config == null)
            {
                logger.LogWarning("Failed to deserialize configuration from {ConfigPath}, using default configuration", configPath);
                return Default;
            }

            // Validate and apply defaults for missing values
            ValidateAndApplyDefaults(config, logger);

            return config;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading configuration from {ConfigPath}, using default configuration", configPath);
            return Default;
        }
    }

    /// <summary>
    /// Validates configuration and applies defaults for missing critical values
    /// </summary>
    private static void ValidateAndApplyDefaults(ModelConfig config, ILogger logger)
    {
        if (config.ContextLength == 0)
        {
            logger.LogWarning("Context length is 0, setting to default value of 2048");
            config.ContextLength = 2048;
        }

        if (string.IsNullOrEmpty(config.Architecture))
        {
            logger.LogWarning("Architecture is not specified, defaulting to 'llama'");
            config.Architecture = "llama";
        }

        if (string.IsNullOrEmpty(config.BosToken))
        {
            config.BosToken = "<s>";
        }

        if (string.IsNullOrEmpty(config.EosToken))
        {
            config.EosToken = "</s>";
        }

        // Validate context length reasonable bounds
        if (config.ContextLength > 1_000_000)
        {
            logger.LogWarning("Context length {ContextLength} is very large, this may cause memory issues", config.ContextLength);
        }
    }

    /// <summary>
    /// Detects the model family based on architecture and other parameters
    /// </summary>
    public string DetectModelFamily()
    {
        if (!string.IsNullOrEmpty(ModelFamily))
        {
            return ModelFamily;
        }

        return Architecture.ToLowerInvariant() switch
        {
            "llama" or "llamaforcausallm" => "llama",
            "mistral" or "mistralforcausallm" => "mistral",
            "codellama" => "codellama",
            "phi" or "phiforcausallm" => "phi",
            "gemma" or "gemmaforcausallm" => "gemma",
            "qwen" or "qwen2" or "qwenforcausallm" => "qwen",
            _ => "llama" // Default fallback
        };
    }

    /// <summary>
    /// Suggests optimal inference parameters based on model configuration
    /// </summary>
    public InferenceParameters SuggestOptimalParameters()
    {
        var contextSize = Math.Min(ContextLength, 4096); // Conservative default
        var batchSize = Math.Min(contextSize / 4, 512); // 1/4 of context or 512, whichever is smaller

        return new InferenceParameters
        {
            ContextSize = contextSize,
            BatchSize = (uint)batchSize,
            GpuLayers = PreferredGpuLayers,
            Threads = PreferredThreads ?? Environment.ProcessorCount,
            FlashAttention = FlashAttention ?? false,
            UseMmap = UseMmap ?? true,
            UseMlock = UseMlock ?? false
        };
    }
}

/// <summary>
/// RoPE scaling configuration
/// </summary>
public class RopeScaling
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "linear";

    [JsonPropertyName("factor")]
    public float Factor { get; set; } = 1.0f;
}

/// <summary>
/// Suggested inference parameters
/// </summary>
public class InferenceParameters
{
    public uint ContextSize { get; set; }
    public uint BatchSize { get; set; }
    public int? GpuLayers { get; set; }
    public int Threads { get; set; }
    public bool FlashAttention { get; set; }
    public bool UseMmap { get; set; }
    public bool UseMlock { get; set; }
}

public static class ModelConfigExtensions
{
    public static string GetConfigPath(this LocalModelInfo modelInfo)
    {
        var modelDir = Path.GetDirectoryName(modelInfo.FullPath);
        return Path.Combine(modelDir!, "config.json");
    }

    public static ModelConfig LoadConfig(this LocalModelInfo modelInfo, ILogger logger)
    {
        var configPath = modelInfo.GetConfigPath();
        return ModelConfig.LoadFromFile(configPath, logger);
    }
}