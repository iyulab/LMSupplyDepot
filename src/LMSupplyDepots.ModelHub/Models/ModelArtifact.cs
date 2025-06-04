namespace LMSupplyDepots.ModelHub.Models;

/// <summary>
/// Represents a specific artifact within a model repository
/// </summary>
public class ModelArtifact
{
    /// <summary>
    /// Name of the artifact
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Format of the artifact (gguf, safetensors, etc.)
    /// </summary>
    public string Format { get; set; } = string.Empty;

    /// <summary>
    /// Size of the artifact in bytes
    /// </summary>
    public long SizeInBytes { get; set; }

    /// <summary>
    /// Collection of file paths for this artifact
    /// </summary>
    public List<string> FilePaths { get; set; } = new();

    /// <summary>
    /// Additional description or metadata about this artifact
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Quantization bits if applicable (4, 5, 8, etc.)
    /// </summary>
    public int? QuantizationBits { get; set; }

    /// <summary>
    /// Size category (XS, S, M, L, XL) if applicable
    /// </summary>
    public string? SizeCategory { get; set; }

    /// <summary>
    /// Main file path for the artifact (or first file for multi-file artifacts)
    /// </summary>
    public string? MainFilePath => FilePaths.Count > 0 ? FilePaths[0] : null;

    /// <summary>
    /// Creates a copy of this artifact
    /// </summary>
    public ModelArtifact Clone()
    {
        return new ModelArtifact
        {
            Name = Name,
            Format = Format,
            SizeInBytes = SizeInBytes,
            FilePaths = new List<string>(FilePaths),
            Description = Description,
            QuantizationBits = QuantizationBits,
            SizeCategory = SizeCategory
        };
    }
}