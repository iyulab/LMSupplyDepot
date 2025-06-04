namespace LMSupplyDepots.Inference.Utils;

/// <summary>
/// Utility methods for working with models
/// </summary>
public static class ModelUtils
{
    /// <summary>
    /// Validates a model can be used for text generation
    /// </summary>
    public static bool ValidateForTextGeneration(LMModel model)
    {
        if (model == null)
        {
            return false;
        }

        // Check model type
        if (model.Type != ModelType.TextGeneration)
        {
            return false;
        }

        // Check capabilities
        if (!model.Capabilities.SupportsTextGeneration)
        {
            return false;
        }

        // Check if model is available locally
        if (!model.IsLocal)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Validates a model can be used for embedding generation
    /// </summary>
    public static bool ValidateForEmbedding(LMModel model)
    {
        if (model == null)
        {
            return false;
        }

        // Check capabilities
        if (!model.Capabilities.SupportsEmbeddings)
        {
            return false;
        }

        // Check if model is available locally
        if (!model.IsLocal)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Gets a normalized model identifier
    /// </summary>
    public static string NormalizeModelIdentifier(string modelId)
    {
        if (string.IsNullOrEmpty(modelId))
        {
            return string.Empty;
        }

        // Replace file extensions
        if (modelId.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
        {
            return modelId[..^5];
        }

        if (modelId.EndsWith(".ggml", StringComparison.OrdinalIgnoreCase))
        {
            return modelId[..^5];
        }

        if (modelId.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
        {
            return modelId[..^4];
        }

        return modelId;
    }

    /// <summary>
    /// Gets the expected format for a model based on file path
    /// </summary>
    public static string GetModelFormatFromPath(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return string.Empty;
        }

        if (filePath.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
        {
            return "GGUF";
        }

        if (filePath.EndsWith(".ggml", StringComparison.OrdinalIgnoreCase))
        {
            return "GGML";
        }

        if (filePath.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) ||
            filePath.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase))
        {
            return "SafeTensors";
        }

        // Check if it's a directory that might contain a model
        if (Directory.Exists(filePath))
        {
            // Look for model files
            if (Directory.GetFiles(filePath, "*.gguf").Any())
            {
                return "GGUF";
            }

            if (Directory.GetFiles(filePath, "*.ggml").Any())
            {
                return "GGML";
            }

            if (Directory.GetFiles(filePath, "*.bin").Any() ||
                Directory.GetFiles(filePath, "*.safetensors").Any())
            {
                return "SafeTensors";
            }
        }

        return string.Empty;
    }
}