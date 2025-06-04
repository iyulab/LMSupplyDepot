using LMSupplyDepots.External.HuggingFace.Models;

namespace LMSupplyDepots.External.HuggingFace.Common;

/// <summary>
/// Provides validation methods for model tags.
/// </summary>
public static class ModelTagValidation
{
    private static readonly HashSet<string> TextGenerationTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "text-generation",
        "text-generation-inference"
    };

    private static readonly HashSet<string> EmbeddingTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "feature-extraction",
        "sentence-similarity",
        "sentence-transformers"
    };

    /// <summary>
    /// Checks if the model is a text generation model based on its tags.
    /// </summary>
    public static bool IsTextGenerationModel(HuggingFaceModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Tags.Any(tag => TextGenerationTags.Contains(tag));
    }

    /// <summary>
    /// Checks if the model is an embedding model based on its tags.
    /// </summary>
    public static bool IsEmbeddingModel(HuggingFaceModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Tags.Any(tag => EmbeddingTags.Contains(tag));
    }

    /// <summary>
    /// Verifies that the model has the expected model type based on its tags.
    /// </summary>
    public static bool VerifyModelType(HuggingFaceModel model, ModelType expectedType)
    {
        return expectedType switch
        {
            ModelType.TextGeneration => IsTextGenerationModel(model),
            ModelType.Embedding => IsEmbeddingModel(model),
            _ => throw new ArgumentException($"Unsupported model type: {expectedType}", nameof(expectedType))
        };
    }
}