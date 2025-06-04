using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace LMSupplyDepots.External.LLamaEngine.Models;

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

    public static ModelConfig Default => new()
    {
        Architecture = "llama",
        ContextLength = 2048,
        BosToken = "<s>",
        EosToken = "</s>"
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
            var config = JsonSerializer.Deserialize<ModelConfig>(jsonString);

            if (config == null)
            {
                logger.LogWarning("Failed to deserialize configuration from {ConfigPath}, using default configuration", configPath);
                return Default;
            }

            return config;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading configuration from {ConfigPath}, using default configuration", configPath);
            return Default;
        }
    }
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