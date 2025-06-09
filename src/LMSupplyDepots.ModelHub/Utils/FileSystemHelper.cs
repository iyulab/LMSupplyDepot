namespace LMSupplyDepots.ModelHub.Utils;

/// <summary>
/// Provides utilities for managing model directory and file structures
/// </summary>
public static class FileSystemHelper
{
    /// <summary>
    /// Default file extension for model files (GGUF format)
    /// </summary>
    public const string ModelFileExtension = ".gguf";

    /// <summary>
    /// File extension for model metadata files
    /// </summary>
    public const string MetadataFileExtension = ".json";

    /// <summary>
    /// File extension for download status files
    /// </summary>
    public const string DownloadStatusFileExtension = ".download";

    /// <summary>
    /// Model file formats in order of preference
    /// </summary>
    public static readonly string[] PreferredModelFormats = [
        ".gguf",       // GGUF for local inference
        ".safetensors", // SafeTensors (more widely supported, safer to load)
        ".bin"         // Standard HuggingFace binary format
    ];

    /// <summary>
    /// Gets the model directory path based on collection ID
    /// </summary>
    public static string GetModelDirectoryPath(string collectionId, string basePath)
    {
        if (string.IsNullOrWhiteSpace(collectionId))
        {
            throw new ArgumentException("Collection ID cannot be empty", nameof(collectionId));
        }

        return Path.Combine(basePath, "models", collectionId.ToFileNameSafe());
    }

    /// <summary>
    /// Gets the model directory path for a model identifier
    /// </summary>
    public static string GetModelDirectoryPath(ModelIdentifier modelId, string basePath)
    {
        return GetModelDirectoryPath(modelId.CollectionId, basePath);
    }

    /// <summary>
    /// Gets the model directory path for a model (legacy support)
    /// </summary>
    public static string GetModelDirectoryPath(string modelId, ModelType modelType, string basePath)
    {
        if (ModelIdentifier.TryParse(modelId, out var identifier))
        {
            return GetModelDirectoryPath(identifier, basePath);
        }

        // Legacy fallback
        return GetModelDirectoryPath(modelId, basePath);
    }

    /// <summary>
    /// Gets information about the directory structure for a model
    /// </summary>
    public static ModelFileStructure GetModelFileStructure(ModelIdentifier modelId, string basePath)
    {
        var modelsPath = Path.Combine(basePath, "models");
        var collectionPath = Path.Combine(modelsPath, modelId.CollectionId.ToFileNameSafe());

        return new ModelFileStructure
        {
            BasePath = basePath,
            ModelsPath = modelsPath,
            ModelNamePath = collectionPath,

            ModelId = modelId.ToString(),
            Registry = modelId.Registry,
            Publisher = modelId.Publisher,
            ModelName = modelId.ModelName,
            ArtifactName = modelId.ArtifactName,
            Format = modelId.Format,
            ModelType = ModelType.TextGeneration // Will be determined from metadata
        };
    }

    /// <summary>
    /// Gets the download status file path for a model artifact
    /// </summary>
    public static string GetDownloadStatusFilePath(ModelIdentifier modelId, string basePath)
    {
        var structure = GetModelFileStructure(modelId, basePath);
        var fileName = $"{modelId.ArtifactName}{DownloadStatusFileExtension}";
        return Path.Combine(structure.ModelNamePath, fileName);
    }

    /// <summary>
    /// Gets the path to the metadata file for a model
    /// </summary>
    public static string GetMetadataFilePath(ModelIdentifier modelId, string basePath)
    {
        var structure = GetModelFileStructure(modelId, basePath);
        string fileName = $"{modelId.ArtifactName}{MetadataFileExtension}";
        return Path.Combine(structure.ModelNamePath, fileName);
    }

    /// <summary>
    /// Gets the path to the metadata file for a collection and artifact
    /// </summary>
    public static string GetMetadataFilePath(string collectionId, string artifactName, string basePath)
    {
        var collectionPath = GetModelDirectoryPath(collectionId, basePath);
        return Path.Combine(collectionPath, $"{artifactName}{MetadataFileExtension}");
    }

    /// <summary>
    /// Creates all necessary directories for a model
    /// </summary>
    public static void EnsureModelDirectoriesExist(ModelIdentifier modelId, string basePath)
    {
        var structure = GetModelFileStructure(modelId, basePath);
        Directory.CreateDirectory(structure.ModelsPath);
        Directory.CreateDirectory(structure.ModelNamePath);
    }

    /// <summary>
    /// Creates all necessary directories for a collection
    /// </summary>
    public static void EnsureModelDirectoriesExist(string collectionId, string basePath)
    {
        var modelsPath = Path.Combine(basePath, "models");
        var collectionPath = GetModelDirectoryPath(collectionId, basePath);

        Directory.CreateDirectory(modelsPath);
        Directory.CreateDirectory(collectionPath);
    }

    /// <summary>
    /// Ensures base directories exist
    /// </summary>
    public static void EnsureBaseDirectoriesExists(string basePath)
    {
        Directory.CreateDirectory(basePath);
        Directory.CreateDirectory(Path.Combine(basePath, "models"));
    }

    /// <summary>
    /// Finds the main model file in a directory
    /// </summary>
    public static string? FindMainModelFile(string modelDirectory)
    {
        if (!Directory.Exists(modelDirectory))
        {
            return null;
        }

        foreach (var format in PreferredModelFormats)
        {
            var files = Directory.GetFiles(modelDirectory, $"*{format}", SearchOption.TopDirectoryOnly);
            if (files.Length > 0)
            {
                return files.OrderByDescending(f => new FileInfo(f).Length).First();
            }
        }

        return null;
    }

    /// <summary>
    /// Checks if a directory contains valid model files
    /// </summary>
    public static bool ContainsModelFiles(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return false;
        }

        foreach (var format in PreferredModelFormats)
        {
            if (Directory.GetFiles(directory, $"*{format}", SearchOption.TopDirectoryOnly).Length > 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Verifies that a model file exists and returns its actual path
    /// </summary>
    public static string? VerifyModelFilePath(string modelPath, ModelType modelType)
    {
        if (File.Exists(modelPath))
        {
            string extension = Path.GetExtension(modelPath).ToLowerInvariant();
            if (PreferredModelFormats.Contains(extension))
            {
                return modelPath;
            }
        }

        if (Directory.Exists(modelPath))
        {
            return FindMainModelFile(modelPath);
        }

        return null;
    }

    /// <summary>
    /// Gets all model files in a directory with their sizes
    /// </summary>
    public static Dictionary<string, long> GetModelFilesWithSizes(string directory)
    {
        var result = new Dictionary<string, long>();

        if (!Directory.Exists(directory))
        {
            return result;
        }

        foreach (var format in PreferredModelFormats)
        {
            foreach (var file in Directory.GetFiles(directory, $"*{format}", SearchOption.TopDirectoryOnly))
            {
                long size = new FileInfo(file).Length;
                result[file] = size;
            }
        }

        return result;
    }

    /// <summary>
    /// Determines if a file is likely a model file based on extension and minimum size
    /// </summary>
    public static bool IsLikelyModelFile(string filePath, long minimumSize = 1024 * 1024)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        if (!PreferredModelFormats.Contains(extension))
        {
            return false;
        }

        var fileInfo = new FileInfo(filePath);
        return fileInfo.Length >= minimumSize;
    }

    /// <summary>
    /// Scans for all model metadata files in the models directory
    /// </summary>
    public static IEnumerable<string> FindAllModelMetadataFiles(string basePath)
    {
        var modelsPath = Path.Combine(basePath, "models");
        if (!Directory.Exists(modelsPath))
        {
            yield break;
        }

        // Scan all collection directories for metadata files
        foreach (var collectionDir in Directory.GetDirectories(modelsPath))
        {
            var metadataFiles = Directory.GetFiles(collectionDir, $"*{MetadataFileExtension}", SearchOption.TopDirectoryOnly);
            foreach (var metadataFile in metadataFiles)
            {
                yield return metadataFile;
            }
        }
    }

    /// <summary>
    /// Gets collection ID from a metadata file path
    /// </summary>
    public static string? GetCollectionIdFromMetadataPath(string metadataFilePath, string basePath)
    {
        var modelsPath = Path.Combine(basePath, "models");
        var relativePath = Path.GetRelativePath(modelsPath, metadataFilePath);

        var pathParts = relativePath.Split(Path.DirectorySeparatorChar);
        if (pathParts.Length >= 2)
        {
            // Return the collection directory name (first part of the path)
            return pathParts[0].FromFileNameSafe();
        }

        return null;
    }

    /// <summary>
    /// Creates a safe file name by replacing invalid characters
    /// </summary>
    public static string ToFileNameSafe(this string input)
    {
        if (string.IsNullOrEmpty(input))
            return "unknown";

        var invalidChars = Path.GetInvalidFileNameChars().Concat(Path.GetInvalidPathChars()).ToArray();
        return new string(input.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
    }

    /// <summary>
    /// Converts a safe file name back to original format
    /// </summary>
    public static string FromFileNameSafe(this string input)
    {
        if (string.IsNullOrEmpty(input))
            return "";

        // For now, just return as-is since we're using simple underscore replacement
        // In the future, we might need more sophisticated encoding/decoding
        return input.Replace("_", "/");
    }
}