using System.Text.RegularExpressions;

namespace LMSupplyDepots.External.HuggingFace.Models;

/// <summary>
/// Provides extension methods and filters for HuggingFace model files.
/// </summary>
public static partial class ModelFileFilters
{
    /// <summary>
    /// Regular expression pattern for matching model weight files.
    /// </summary>
    [GeneratedRegex(@"\.(bin|safetensors|gguf|pt|pth|ckpt|model)$", RegexOptions.IgnoreCase)]
    private static partial Regex ModelWeightPatternGenerator();
    public static readonly Regex ModelWeightPattern = ModelWeightPatternGenerator();

    /// <summary>
    /// Regular expression pattern for matching GGUF model files.
    /// </summary>
    [GeneratedRegex(@"\.gguf$", RegexOptions.IgnoreCase)]
    private static partial Regex GgufModelPatternGenerator();
    public static readonly Regex GgufModelPattern = GgufModelPatternGenerator();

    /// <summary>
    /// Regular expression pattern for matching model configuration files.
    /// </summary>
    [GeneratedRegex(@"\.(json|yaml|yml)$", RegexOptions.IgnoreCase)]
    private static partial Regex ConfigFilePatternGenerator();
    public static readonly Regex ConfigFilePattern = ConfigFilePatternGenerator();

    /// <summary>
    /// Regular expression pattern for matching tokenizer files.
    /// </summary>
    [GeneratedRegex(@"tokenizer\.(json|model)$", RegexOptions.IgnoreCase)]
    private static partial Regex TokenizerFilePatternGenerator();
    public static readonly Regex TokenizerFilePattern = TokenizerFilePatternGenerator();

    /// <summary>
    /// Gets the paths of model weight files in the repository.
    /// </summary>
    public static string[] GetModelWeightPaths(this HuggingFaceModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.GetFilePaths(ModelWeightPattern);
    }

    /// <summary>
    /// Gets the paths of GGUF model files in the repository.
    /// </summary>
    public static string[] GetGgufModelPaths(this HuggingFaceModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.GetFilePaths(GgufModelPattern);
    }

    /// <summary>
    /// Checks if the model has any GGUF format files.
    /// </summary>
    public static bool HasGgufFiles(this HuggingFaceModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.GetGgufModelPaths().Length > 0;
    }

    /// <summary>
    /// Gets the paths of configuration files in the repository.
    /// </summary>
    public static string[] GetConfigurationPaths(this HuggingFaceModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.GetFilePaths(ConfigFilePattern);
    }

    /// <summary>
    /// Gets the paths of tokenizer files in the repository.
    /// </summary>
    public static string[] GetTokenizerPaths(this HuggingFaceModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.GetFilePaths(TokenizerFilePattern);
    }

    /// <summary>
    /// Gets the paths of essential model files (weights, configurations, and tokenizers) in the repository.
    /// </summary>
    public static string[] GetEssentialModelPaths(this HuggingFaceModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var weightFiles = model.GetModelWeightPaths();
        var configFiles = model.GetConfigurationPaths();
        var tokenizerFiles = model.GetTokenizerPaths();

        return [.. weightFiles.Concat(configFiles).Concat(tokenizerFiles).Distinct()];
    }
}