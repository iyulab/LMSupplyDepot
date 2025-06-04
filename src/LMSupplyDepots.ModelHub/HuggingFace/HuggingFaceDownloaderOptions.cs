namespace LMSupplyDepots.ModelHub.HuggingFace;

/// <summary>
/// Configuration options for the HuggingFace model downloader
/// </summary>
public class HuggingFaceDownloaderOptions
{
    /// <summary>
    /// Gets or sets the HuggingFace API token
    /// </summary>
    public string? ApiToken { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of concurrent file downloads
    /// </summary>
    public int MaxConcurrentFileDownloads { get; set; } = 4;

    /// <summary>
    /// Gets or sets the timeout for API requests
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets the maximum number of retry attempts for failed requests
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Gets or sets the buffer size for download operations
    /// </summary>
    public int BufferSize { get; set; } = 8192;

    /// <summary>
    /// Gets or sets whether to verify checksums after downloading files
    /// </summary>
    public bool VerifyChecksums { get; set; } = true;

    /// <summary>
    /// Gets or sets the allowed file extensions for text generation models
    /// </summary>
    public string[] TextGenerationFileExtensions { get; set; } = [".gguf", ".bin", ".safetensors"];

    /// <summary>
    /// Gets or sets the allowed file extensions for embedding models
    /// </summary>
    public string[] EmbeddingFileExtensions { get; set; } = [".gguf", ".bin", ".safetensors"];

    /// <summary>
    /// Gets or sets the maximum number of model files to download (0 for unlimited)
    /// </summary>
    public int MaxModelFiles { get; set; } = 0;

    /// <summary>
    /// Gets or sets whether to include model metadata files in downloads
    /// </summary>
    public bool IncludeMetadataFiles { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to prioritize downloading GGUF files over other formats
    /// </summary>
    public bool PrioritizeGgufFiles { get; set; } = true;
}