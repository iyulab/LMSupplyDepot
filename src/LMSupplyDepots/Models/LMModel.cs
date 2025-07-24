using System.Text.Json.Serialization;

namespace LMSupplyDepots.Models;

/// <summary>
/// Core representation of a language model with essential metadata
/// </summary>
public class LMModel
{
    [JsonIgnore]
    public string Key => string.IsNullOrEmpty(Alias) ? Id : Alias;

    /// <summary>
    /// Alias for the model
    /// </summary>
    public string? Alias { get; set; } = string.Empty;

    /// <summary>
    /// Unique identifier for the model
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the model
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Brief description of the model
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Version information for the model
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Path to the model file or directory on disk if available locally
    /// </summary>
    public string? LocalPath { get; set; }

    /// <summary>
    /// Model capabilities
    /// </summary>
    public LMModelCapabilities Capabilities { get; set; } = new();

    /// <summary>
    /// Registry of the model (e.g., "hf", "local")
    /// </summary>
    public string Registry { get; set; } = string.Empty;

    /// <summary>
    /// Repository ID of the model
    /// </summary>
    public string RepoId { get; set; } = string.Empty;

    /// <summary>
    /// The type of model
    /// </summary>
    public ModelType Type { get; set; } = ModelType.TextGeneration;

    /// <summary>
    /// Format of the model (GGUF, GGML, SafeTensors, etc.)
    /// </summary>
    public string Format { get; set; } = string.Empty;

    /// <summary>
    /// Specific artifact name within the model repository
    /// </summary>
    public string ArtifactName { get; set; } = string.Empty;

    /// <summary>
    /// Size of the model in bytes
    /// </summary>
    public long SizeInBytes { get; set; } = 0;

    /// <summary>
    /// Collection of file paths for multi-file models
    /// </summary>
    public List<string> FilePaths { get; set; } = new();

    /// <summary>
    /// Determines if the model is available locally
    /// </summary>
    public bool IsLocal => !string.IsNullOrEmpty(LocalPath) &&
                          (Directory.Exists(LocalPath) || File.Exists(LocalPath));

    /// <summary>
    /// Indicates whether the model is currently loaded in memory
    /// This property should not be persisted to storage as it represents runtime state
    /// </summary>
    [JsonIgnore]
    public bool IsLoaded { get; set; } = false;

    /// <summary>
    /// Timestamp when the model was loaded into memory
    /// </summary>
    [JsonIgnore]
    public DateTime? LoadedAt { get; set; }

    /// <summary>
    /// Sets the model as loaded with current timestamp
    /// </summary>
    public void SetLoaded()
    {
        IsLoaded = true;
        LoadedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Sets the model as unloaded and clears timestamp
    /// </summary>
    public void SetUnloaded()
    {
        IsLoaded = false;
        LoadedAt = null;
    }
}