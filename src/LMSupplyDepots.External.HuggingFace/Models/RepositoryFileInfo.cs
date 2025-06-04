using System.Text.Json.Serialization;

namespace LMSupplyDepots.External.HuggingFace.Models;

/// <summary>
/// Represents a file or directory in a Hugging Face repository
/// </summary>
public class RepositoryFileInfo
{
    /// <summary>
    /// The type of the item (file or directory)
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Object ID
    /// </summary>
    [JsonPropertyName("oid")]
    public string Oid { get; set; } = string.Empty;

    /// <summary>
    /// File size in bytes
    /// </summary>
    [JsonPropertyName("size")]
    public long Size { get; set; }

    /// <summary>
    /// File path relative to the repository root
    /// </summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// LFS information if the file is stored using Git LFS
    /// </summary>
    [JsonPropertyName("lfs")]
    public LfsInfo? Lfs { get; set; }

    /// <summary>
    /// LFS information structure
    /// </summary>
    public class LfsInfo
    {
        /// <summary>
        /// LFS object ID
        /// </summary>
        [JsonPropertyName("oid")]
        public string Oid { get; set; } = string.Empty;

        /// <summary>
        /// Actual size of the file
        /// </summary>
        [JsonPropertyName("size")]
        public long Size { get; set; }

        /// <summary>
        /// Size of the LFS pointer
        /// </summary>
        [JsonPropertyName("pointerSize")]
        public long PointerSize { get; set; }
    }

    /// <summary>
    /// Gets the effective size of the file
    /// </summary>
    public long GetEffectiveSize() => Lfs?.Size ?? Size;

    /// <summary>
    /// Checks if this item is a directory
    /// </summary>
    public bool IsDirectory => Type == "directory" || Type == "tree";

    /// <summary>
    /// Checks if this item is a file
    /// </summary>
    public bool IsFile => Type == "file" || Type == "blob";
}