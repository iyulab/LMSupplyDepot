using LMSupplyDepots.External.HuggingFace.Models;
using LMSupplyDepots.External.HuggingFace.Client;

namespace LMSupplyDepots.ModelHub.HuggingFace;

/// <summary>
/// Helper class for HuggingFace specific operations
/// </summary>
public static class HuggingFaceHelper
{
    private static readonly Regex _sourceIdRegex = new(@"^(hf|huggingface):(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex _quantizationRegex = new(@"Q(\d+)(_[KM])?(_S|_M|_L|_XL)?", RegexOptions.Compiled);
    private static readonly Regex _sizeCategoryRegex = new(@"(_XS|_S|_M|_L|_XL|-xs|-small|-medium|-large|-xl)$", RegexOptions.Compiled);
    private static readonly Regex _multiFileRegex = new(@"(.*?)[.-](\d{5})-of-(\d{5})$", RegexOptions.Compiled);

    /// <summary>
    /// Normalizes a source ID to remove the HuggingFace prefix
    /// </summary>
    public static string NormalizeSourceId(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
            return sourceId;

        var match = _sourceIdRegex.Match(sourceId);
        if (match.Success)
        {
            return match.Groups[2].Value;
        }

        return sourceId;
    }

    /// <summary>
    /// Splits a source ID into repository ID and artifact name
    /// </summary>
    public static (string repoId, string? artifactName) NormalizeAndSplitSourceId(string sourceId)
    {
        var match = _sourceIdRegex.Match(sourceId);
        string normalizedId = match.Success ? match.Groups[2].Value : sourceId;

        string? artifactName = null;
        string repoId = normalizedId;

        var lastSlashIndex = normalizedId.LastIndexOf('/');
        if (lastSlashIndex >= 0)
        {
            var parts = normalizedId.Split('/');

            if (parts.Length >= 3)
            {
                artifactName = parts[parts.Length - 1];
                repoId = string.Join("/", parts.Take(parts.Length - 1));
            }
            else if (parts.Length == 2)
            {
                repoId = normalizedId;
                artifactName = null;
            }
        }

        return (repoId, artifactName);
    }

    /// <summary>
    /// Gets actual file size from repository file sizes
    /// </summary>
    public static long? GetActualFileSize(Dictionary<string, long> repositoryFileSizes, string filePath)
    {
        if (repositoryFileSizes.TryGetValue(filePath, out var size) && size > 0)
        {
            return size;
        }
        return null;
    }

    /// <summary>
    /// Determines the model type based on its tags and capabilities
    /// </summary>
    public static ModelType DetermineModelType(HuggingFaceModel hfModel)
    {
        var tags = hfModel.Tags.Select(t => t.ToLowerInvariant()).ToList();

        if (External.HuggingFace.Common.ModelTagValidation.IsTextGenerationModel(hfModel))
        {
            return ModelType.TextGeneration;
        }

        if (External.HuggingFace.Common.ModelTagValidation.IsEmbeddingModel(hfModel))
        {
            return ModelType.Embedding;
        }

        if (tags.Any(t => t.Contains("vision") || t.Contains("image-to-text")))
        {
            Debug.WriteLine($"Model {hfModel.ModelId} is a vision model.");
        }

        return ModelType.TextGeneration;
    }

    /// <summary>
    /// Gets the format of a model based on its files
    /// </summary>
    public static string GetModelFormat(HuggingFaceModel hfModel)
    {
        if (hfModel.HasGgufFiles())
        {
            return "GGUF";
        }

        var weightFiles = hfModel.GetModelWeightPaths();
        if (weightFiles.Any(f => f.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase)))
        {
            return "SafeTensors";
        }

        if (weightFiles.Any(f => f.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)))
        {
            return "HF Binary";
        }

        return "Unknown";
    }

    /// <summary>
    /// Extracts a description for the model from its metadata
    /// </summary>
    public static string GetModelDescription(HuggingFaceModel hfModel)
    {
        var description = string.Empty;

        if (hfModel.HasProperty("modelCardData") &&
            hfModel.GetProperty<JsonElement>("modelCardData") is JsonElement modelCard &&
            modelCard.TryGetProperty("model-index", out var modelIndex) &&
            modelIndex.TryGetProperty("description", out var descElement))
        {
            description = descElement.GetString() ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            var tags = string.Join(", ", hfModel.Tags.Take(5));
            description = $"{hfModel.ModelId} - Tags: {tags} - Created by {hfModel.Author}";
        }

        return description;
    }

    /// <summary>
    /// Determines the maximum context length of a model based on its metadata or name
    /// </summary>
    public static int GetMaxContextLength(HuggingFaceModel hfModel)
    {
        if (hfModel.GGUF.Count > 0 &&
            hfModel.GGUF.TryGetValue("general.context_length", out var contextLengthValue))
        {
            if (contextLengthValue.ValueKind == JsonValueKind.Number)
            {
                return contextLengthValue.GetInt32();
            }
        }

        var modelName = hfModel.ModelId.ToLowerInvariant();

        if (modelName.Contains("7b") || modelName.Contains("1b") || modelName.Contains("3b"))
            return 4096;

        if (modelName.Contains("13b") || modelName.Contains("14b"))
            return 8192;

        if (modelName.Contains("30b") || modelName.Contains("33b") || modelName.Contains("34b"))
            return 8192;

        if (modelName.Contains("70b") || modelName.Contains("65b") || modelName.Contains("mixtral"))
            return 32768;

        return 4096;
    }

    /// <summary>
    /// Determines the embedding dimension for embedding models
    /// </summary>
    public static int? GetEmbeddingDimension(HuggingFaceModel hfModel)
    {
        if (External.HuggingFace.Common.ModelTagValidation.IsEmbeddingModel(hfModel))
        {
            var match = Regex.Match(hfModel.ModelId, @"(\d+)d");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int dimension))
            {
                return dimension;
            }

            if (hfModel.ModelId.Contains("minilm"))
                return 384;

            if (hfModel.ModelId.Contains("mpnet"))
                return 768;

            if (hfModel.ModelId.Contains("e5"))
                return 1024;

            return 768;
        }

        return null;
    }

    /// <summary>
    /// Creates a lightweight collection for discovery (without file sizes)
    /// </summary>
    public static LMCollection CreateLightweightCollection(HuggingFaceModel hfModel)
    {
        var modelType = DetermineModelType(hfModel);
        var format = GetModelFormat(hfModel);
        var collectionId = hfModel.ModelId;

        var collection = new LMCollection
        {
            Id = $"hf:{collectionId}",
            Hub = "hf",
            CollectionId = collectionId,
            Name = string.IsNullOrEmpty(collectionId) ? "Unknown Model" : Path.GetFileName(collectionId),
            Type = modelType,
            DefaultFormat = format,
            Version = hfModel.LastModified.ToString("yyyyMMdd"),
            Description = GetModelDescription(hfModel),
            Publisher = hfModel.Author,
            Tags = hfModel.Tags.ToList(),
            Downloads = hfModel.Downloads,
            Likes = hfModel.Likes,
            CreatedAt = hfModel.CreatedAt.DateTime,
            LastModified = hfModel.LastModified.DateTime,
            License = hfModel.GetStringProperty("license"),
            Language = hfModel.GetStringProperty("language"),
            Capabilities = new LMModelCapabilities
            {
                SupportsTextGeneration = modelType == ModelType.TextGeneration,
                SupportsEmbeddings = modelType == ModelType.Embedding,
                SupportsImageUnderstanding = hfModel.Tags.Any(t =>
                    t.Contains("vision") || t.Contains("image") || t.Contains("multimodal")),
                MaxContextLength = GetMaxContextLength(hfModel),
                EmbeddingDimension = GetEmbeddingDimension(hfModel)
            }
        };

        // Extract basic artifacts without file sizes for fast discovery
        collection.Artifacts = ExtractBasicArtifactsFromSiblings(hfModel.Siblings, format);

        if (collection.Artifacts.Count == 0)
        {
            collection.Artifacts.Add(new ModelArtifact
            {
                Name = Path.GetFileName(collectionId),
                Format = format,
                Description = $"Default {format} model",
                SizeInBytes = 0,
                FilePaths = new List<string>()
            });
        }

        return collection;
    }

    /// <summary>
    /// Creates a detailed collection with file sizes for info endpoint
    /// </summary>
    public static async Task<LMCollection> CreateDetailedCollectionAsync(
        HuggingFaceModel hfModel,
        Dictionary<string, long>? repositoryFileSizes,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var modelType = DetermineModelType(hfModel);
        var format = GetModelFormat(hfModel);
        var collectionId = hfModel.ModelId;

        var collection = new LMCollection
        {
            Id = $"hf:{collectionId}",
            Hub = "hf",
            CollectionId = collectionId,
            Name = string.IsNullOrEmpty(collectionId) ? "Unknown Model" : Path.GetFileName(collectionId),
            Type = modelType,
            DefaultFormat = format,
            Version = hfModel.LastModified.ToString("yyyyMMdd"),
            Description = GetModelDescription(hfModel),
            Publisher = hfModel.Author,
            Tags = hfModel.Tags.ToList(),
            Downloads = hfModel.Downloads,
            Likes = hfModel.Likes,
            CreatedAt = hfModel.CreatedAt.DateTime,
            LastModified = hfModel.LastModified.DateTime,
            License = hfModel.GetStringProperty("license"),
            Language = hfModel.GetStringProperty("language"),
            Capabilities = new LMModelCapabilities
            {
                SupportsTextGeneration = modelType == ModelType.TextGeneration,
                SupportsEmbeddings = modelType == ModelType.Embedding,
                SupportsImageUnderstanding = hfModel.Tags.Any(t =>
                    t.Contains("vision") || t.Contains("image") || t.Contains("multimodal")),
                MaxContextLength = GetMaxContextLength(hfModel),
                EmbeddingDimension = GetEmbeddingDimension(hfModel)
            }
        };

        // Extract detailed artifacts with file sizes
        collection.Artifacts = await ExtractDetailedArtifactsFromModelAsync(
            hfModel, repositoryFileSizes, logger, cancellationToken);

        if (collection.Artifacts.Count == 0)
        {
            collection.Artifacts.Add(new ModelArtifact
            {
                Name = Path.GetFileName(collectionId),
                Format = format,
                Description = $"Default {format} model",
                SizeInBytes = 0,
                FilePaths = new List<string>()
            });
        }

        return collection;
    }

    /// <summary>
    /// Extracts basic artifact information without file sizes for fast discovery
    /// </summary>
    public static List<ModelArtifact> ExtractBasicArtifactsFromSiblings(List<Sibling> siblings, string defaultFormat)
    {
        var artifacts = new List<ModelArtifact>();

        if (siblings == null || siblings.Count == 0)
        {
            return artifacts;
        }

        var modelFiles = siblings
            .Where(s => !string.IsNullOrEmpty(s.Filename) && IsModelFile(s.Filename))
            .ToList();

        if (modelFiles.Count == 0)
        {
            return artifacts;
        }

        // Group files by artifact name (handle split files)
        var fileGroups = GroupFilesByArtifact(modelFiles);

        foreach (var group in fileGroups)
        {
            var artifactName = group.Key;
            var files = group.Value;
            var firstFile = files.First();

            var format = Path.GetExtension(firstFile.Filename).TrimStart('.').ToLowerInvariant();
            var (quantBits, sizeCategory) = ParseArtifactInfo(artifactName);

            var artifact = new ModelArtifact
            {
                Name = artifactName,
                Format = format,
                FilePaths = files.Select(f => f.Filename).ToList(),
                Description = GetArtifactDescription(artifactName, format, files.Count > 1),
                QuantizationBits = quantBits,
                SizeCategory = sizeCategory,
                SizeInBytes = 0 // No size info for fast discovery
            };

            artifacts.Add(artifact);
        }

        return artifacts.OrderBy(a => a.Name).ToList();
    }

    /// <summary>
    /// Extracts artifacts from a HuggingFace model with actual file sizes for detailed info
    /// </summary>
    public static Task<List<ModelArtifact>> ExtractDetailedArtifactsFromModelAsync(
        HuggingFaceModel hfModel,
        Dictionary<string, long>? repositoryFileSizes,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var artifacts = new List<ModelArtifact>();

        if (hfModel.Siblings == null || hfModel.Siblings.Count == 0)
        {
            logger?.LogWarning("No siblings found for model {ModelId}", hfModel.ModelId);
            return Task.FromResult(artifacts);
        }

        // Get all model files from siblings
        var modelFiles = hfModel.Siblings
            .Where(s => !string.IsNullOrEmpty(s.Filename) && IsModelFile(s.Filename))
            .ToList();

        if (modelFiles.Count == 0)
        {
            logger?.LogWarning("No model files found in siblings for {ModelId}", hfModel.ModelId);
            return Task.FromResult(artifacts);
        }

        logger?.LogDebug("Found {Count} model files for {ModelId}", modelFiles.Count, hfModel.ModelId);

        // Group files by artifact name (handle split files)
        var fileGroups = GroupFilesByArtifact(modelFiles);

        foreach (var group in fileGroups)
        {
            var artifactName = group.Key;
            var files = group.Value;
            var firstFile = files.First();

            var format = Path.GetExtension(firstFile.Filename).TrimStart('.').ToLowerInvariant();
            var (quantBits, sizeCategory) = ParseArtifactInfo(artifactName);

            // Calculate total size for this artifact
            long totalSize = 0;
            var filePaths = new List<string>();

            foreach (var file in files)
            {
                filePaths.Add(file.Filename);

                // Get actual file size from repository if available
                if (repositoryFileSizes != null && repositoryFileSizes.TryGetValue(file.Filename, out var fileSize))
                {
                    totalSize += fileSize;
                }
            }

            var artifact = new ModelArtifact
            {
                Name = artifactName,
                Format = format,
                FilePaths = filePaths,
                Description = GetArtifactDescription(artifactName, format, files.Count > 1),
                QuantizationBits = quantBits,
                SizeCategory = sizeCategory,
                SizeInBytes = totalSize
            };

            artifacts.Add(artifact);

            logger?.LogDebug("Created artifact {ArtifactName} with {FileCount} files, total size: {TotalSize} bytes",
                artifactName, files.Count, totalSize);
        }

        return Task.FromResult(artifacts.OrderBy(a => a.Name).ToList());
    }

    /// <summary>
    /// Extracts artifact information from repository files with actual sizes
    /// </summary>
    public static Task<List<ModelArtifact>> ExtractArtifactsAsync(
        List<string> files,
        string defaultFormat,
        Dictionary<string, long>? actualFileSizes = null)
    {
        var artifacts = new List<ModelArtifact>();
        var fileGroups = new Dictionary<string, List<string>>();

        var preferredFiles = files
            .Where(f => IsModelFileExtension(Path.GetExtension(f)))
            .ToList();

        foreach (var file in preferredFiles)
        {
            var extension = Path.GetExtension(file);
            if (string.IsNullOrEmpty(extension)) continue;

            var format = extension.TrimStart('.').ToLowerInvariant();
            var baseName = Path.GetFileNameWithoutExtension(file);

            if (!IsModelFileExtension(extension)) continue;

            var multiFileMatch = _multiFileRegex.Match(baseName);
            if (multiFileMatch.Success)
            {
                var artifactBaseName = multiFileMatch.Groups[1].Value;
                if (!fileGroups.TryGetValue(artifactBaseName, out var group))
                {
                    group = new List<string>();
                    fileGroups[artifactBaseName] = group;
                }
                group.Add(file);
                continue;
            }

            var (quantBits, sizeCategory) = ParseArtifactInfo(baseName);
            var actualSize = actualFileSizes?.GetValueOrDefault(file);

            artifacts.Add(new ModelArtifact
            {
                Name = baseName,
                Format = format,
                FilePaths = new List<string> { file },
                Description = GetArtifactDescription(baseName, format),
                QuantizationBits = quantBits,
                SizeCategory = sizeCategory,
                SizeInBytes = actualSize ?? 0
            });
        }

        foreach (var group in fileGroups)
        {
            if (group.Value.Count > 0)
            {
                var firstFile = group.Value[0];
                var format = Path.GetExtension(firstFile).TrimStart('.').ToLowerInvariant();
                var baseName = group.Key;
                var (quantBits, sizeCategory) = ParseArtifactInfo(baseName);

                var totalSize = actualFileSizes != null
                    ? group.Value.Sum(f => actualFileSizes.GetValueOrDefault(f, 0))
                    : 0;

                artifacts.Add(new ModelArtifact
                {
                    Name = baseName,
                    Format = format,
                    FilePaths = group.Value,
                    Description = GetArtifactDescription(baseName, format, true),
                    QuantizationBits = quantBits,
                    SizeCategory = sizeCategory,
                    SizeInBytes = totalSize
                });
            }
        }

        return Task.FromResult(artifacts);
    }

    /// <summary>
    /// Converts a HuggingFaceModel to our LMModel format
    /// </summary>
    public static LMModel ConvertToLMModel(HuggingFaceModel hfModel)
    {
        var modelType = DetermineModelType(hfModel);
        var format = GetModelFormat(hfModel);

        var model = new LMModel
        {
            Id = $"hf:{hfModel.ModelId}",
            Registry = "hf",
            RepoId = hfModel.ModelId,
            Name = string.IsNullOrEmpty(hfModel.ModelId) ?
                "Unknown Model" : Path.GetFileName(hfModel.ModelId),
            Type = modelType,
            Format = format,
            Version = hfModel.LastModified.ToString("yyyyMMdd"),
            Description = GetModelDescription(hfModel),
            Capabilities = new LMModelCapabilities
            {
                SupportsTextGeneration = modelType == ModelType.TextGeneration,
                SupportsEmbeddings = modelType == ModelType.Embedding,
                SupportsImageUnderstanding = hfModel.Tags.Any(t =>
                    t.Contains("vision") || t.Contains("image") || t.Contains("multimodal")),
                MaxContextLength = GetMaxContextLength(hfModel),
                EmbeddingDimension = GetEmbeddingDimension(hfModel)
            }
        };

        model.ArtifactName = hfModel.ModelId.Split('/').Last();
        return model;
    }

    /// <summary>
    /// Finds files in a HuggingFace model that match an artifact name
    /// </summary>
    public static List<string> FindArtifactFiles(HuggingFaceModel model, string artifactName)
    {
        if (model == null || string.IsNullOrEmpty(artifactName))
            return new List<string>();

        var allFiles = model.GetModelWeightPaths();

        var exactMatches = allFiles.Where(f =>
            Path.GetFileNameWithoutExtension(f).Equals(artifactName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (exactMatches.Count > 0)
            return exactMatches;

        var startingMatches = allFiles.Where(f =>
            Path.GetFileNameWithoutExtension(f).StartsWith(artifactName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (startingMatches.Count > 0)
            return startingMatches;

        var containingMatches = allFiles.Where(f =>
            Path.GetFileNameWithoutExtension(f).Contains(artifactName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (containingMatches.Count > 0)
            return containingMatches;

        return new List<string> { $"{artifactName}.gguf" };
    }

    /// <summary>
    /// Gets available model files from a HuggingFace model
    /// </summary>
    public static List<string> GetAvailableModelFiles(HuggingFaceModel hfModel)
    {
        var files = new List<string>();

        if (hfModel.Siblings != null && hfModel.Siblings.Count > 0)
        {
            foreach (var sibling in hfModel.Siblings)
            {
                if (!string.IsNullOrEmpty(sibling.Filename) && IsModelFile(sibling.Filename))
                {
                    files.Add(sibling.Filename);
                }
            }
        }

        return files;
    }

    /// <summary>
    /// Determines the model type from a repository ID
    /// </summary>
    public static async Task<ModelType> DetermineModelTypeAsync(string repoId, HuggingFaceClient client, CancellationToken cancellationToken)
    {
        try
        {
            var hfModel = await client.FindModelByRepoIdAsync(repoId, cancellationToken);
            return DetermineModelType(hfModel);
        }
        catch
        {
            return ModelType.TextGeneration;
        }
    }

    /// <summary>
    /// Extracts artifacts directly from siblings information with actual sizes
    /// </summary>
    public static List<ModelArtifact> ExtractArtifactsFromSiblings(List<Sibling> siblings, string defaultFormat)
    {
        var artifacts = new List<ModelArtifact>();

        if (siblings == null || siblings.Count == 0)
        {
            return artifacts;
        }

        var ggufFiles = siblings.Where(s => !string.IsNullOrEmpty(s.Filename) &&
                                           s.Filename.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
                               .ToList();

        if (ggufFiles.Count > 0)
        {
            ProcessGgufFiles(ggufFiles, artifacts);
            ProcessMultiFileGroups(ggufFiles, artifacts);
        }

        if (artifacts.Count == 0)
        {
            ProcessOtherFormatFiles(siblings, artifacts);
        }

        if (artifacts.Count == 0 && siblings.Count > 0)
        {
            AddPlaceholderArtifact(siblings, artifacts, defaultFormat);
        }

        return artifacts;
    }

    /// <summary>
    /// Groups files by artifact name, handling split files
    /// </summary>
    private static Dictionary<string, List<Sibling>> GroupFilesByArtifact(List<Sibling> modelFiles)
    {
        var groups = new Dictionary<string, List<Sibling>>();

        foreach (var file in modelFiles)
        {
            var fileName = Path.GetFileNameWithoutExtension(file.Filename);

            // Check if this is a split file (e.g., "model-00001-of-00005")
            var multiFileMatch = _multiFileRegex.Match(fileName);

            string artifactName;
            if (multiFileMatch.Success)
            {
                // Split file - use the base name
                artifactName = multiFileMatch.Groups[1].Value;
            }
            else
            {
                // Single file - use the full name without extension
                artifactName = fileName;
            }

            if (!groups.TryGetValue(artifactName, out var group))
            {
                group = new List<Sibling>();
                groups[artifactName] = group;
            }

            group.Add(file);
        }

        return groups;
    }

    /// <summary>
    /// Determines if a filename represents a model file
    /// </summary>
    private static bool IsModelFile(string filename)
    {
        var extension = Path.GetExtension(filename).ToLowerInvariant();
        return extension == ".gguf" ||
               extension == ".safetensors" ||
               extension == ".bin" ||
               extension == ".ggml" ||
               extension == ".pt" ||
               extension == ".pth";
    }

    /// <summary>
    /// Parses quantization and size information from artifact name
    /// </summary>
    private static (int? quantBits, string? sizeCategory) ParseArtifactInfo(string artifactName)
    {
        int? quantBits = null;
        string? sizeCategory = null;

        var quantMatch = _quantizationRegex.Match(artifactName);
        if (quantMatch.Success)
        {
            quantBits = int.Parse(quantMatch.Groups[1].Value);
        }

        var sizeMatch = _sizeCategoryRegex.Match(artifactName);
        if (sizeMatch.Success)
        {
            sizeCategory = GetNormalizedSizeCategory(sizeMatch.Groups[1].Value);
        }

        return (quantBits, sizeCategory);
    }

    /// <summary>
    /// Generates description for an artifact
    /// </summary>
    private static string GetArtifactDescription(string artifactName, string format, bool isMultiFile = false)
    {
        var parts = new List<string>();

        parts.Add($"{format.ToUpperInvariant()} format");

        if (artifactName.Contains("Q", StringComparison.OrdinalIgnoreCase))
        {
            var quantMatch = _quantizationRegex.Match(artifactName);
            if (quantMatch.Success)
            {
                parts.Add($"Q{quantMatch.Groups[1].Value} quantization");
            }
        }

        var sizeMatch = _sizeCategoryRegex.Match(artifactName);
        if (sizeMatch.Success)
        {
            var sizeCategory = GetNormalizedSizeCategory(sizeMatch.Groups[1].Value);
            parts.Add($"{sizeCategory} size");
        }

        if (isMultiFile)
        {
            parts.Add("Multiple files");
        }

        return string.Join(", ", parts);
    }

    /// <summary>
    /// Normalizes size category string
    /// </summary>
    private static string GetNormalizedSizeCategory(string sizeCategory)
    {
        var normalized = sizeCategory.ToUpperInvariant();

        if (normalized.Contains("XS") || normalized == "-XS")
            return "Extra Small";
        if (normalized.Contains("S") || normalized == "-SMALL")
            return "Small";
        if (normalized.Contains("M") || normalized == "-MEDIUM")
            return "Medium";
        if (normalized.Contains("L") && !normalized.Contains("XL") || normalized == "-LARGE")
            return "Large";
        if (normalized.Contains("XL") || normalized == "-XL")
            return "Extra Large";

        return "Unknown Size";
    }

    private static void ProcessGgufFiles(List<Sibling> ggufFiles, List<ModelArtifact> artifacts)
    {
        foreach (var sibling in ggufFiles)
        {
            var extension = Path.GetExtension(sibling.Filename);
            var format = extension.TrimStart('.').ToLowerInvariant();
            var baseName = Path.GetFileNameWithoutExtension(sibling.Filename);

            var multiFileMatch = _multiFileRegex.Match(baseName);
            if (multiFileMatch.Success)
            {
                continue;
            }

            var (quantBits, sizeCategory) = ParseArtifactInfo(baseName);

            artifacts.Add(new ModelArtifact
            {
                Name = baseName,
                Format = format,
                FilePaths = new List<string> { sibling.Filename },
                Description = GetArtifactDescription(baseName, format),
                QuantizationBits = quantBits,
                SizeCategory = sizeCategory,
                SizeInBytes = 0 // Sibling doesn't have Size property
            });
        }
    }

    private static void ProcessMultiFileGroups(List<Sibling> ggufFiles, List<ModelArtifact> artifacts)
    {
        var fileGroups = new Dictionary<string, List<Sibling>>();
        foreach (var sibling in ggufFiles)
        {
            var baseName = Path.GetFileNameWithoutExtension(sibling.Filename);
            var multiFileMatch = _multiFileRegex.Match(baseName);

            if (multiFileMatch.Success)
            {
                var artifactBaseName = multiFileMatch.Groups[1].Value;

                if (!fileGroups.TryGetValue(artifactBaseName, out var group))
                {
                    group = new List<Sibling>();
                    fileGroups[artifactBaseName] = group;
                }

                group.Add(sibling);
            }
        }

        foreach (var group in fileGroups)
        {
            if (group.Value.Count > 0)
            {
                var firstFile = group.Value[0];
                var format = Path.GetExtension(firstFile.Filename).TrimStart('.').ToLowerInvariant();
                var baseName = group.Key;
                var (quantBits, sizeCategory) = ParseArtifactInfo(baseName);

                // Sibling doesn't have Size property - set to 0 (unknown)
                var totalSize = 0L;

                artifacts.Add(new ModelArtifact
                {
                    Name = baseName,
                    Format = format,
                    FilePaths = group.Value.Select(s => s.Filename).ToList(),
                    Description = GetArtifactDescription(baseName, format, true),
                    QuantizationBits = quantBits,
                    SizeCategory = sizeCategory,
                    SizeInBytes = totalSize
                });
            }
        }
    }

    private static void ProcessOtherFormatFiles(List<Sibling> siblings, List<ModelArtifact> artifacts)
    {
        var modelFiles = siblings.Where(s => !string.IsNullOrEmpty(s.Filename) &&
                                          (s.Filename.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) ||
                                           s.Filename.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase) ||
                                           s.Filename.EndsWith(".pt", StringComparison.OrdinalIgnoreCase) ||
                                           s.Filename.EndsWith(".pth", StringComparison.OrdinalIgnoreCase)))
                              .ToList();

        foreach (var sibling in modelFiles)
        {
            var extension = Path.GetExtension(sibling.Filename);
            var format = extension.TrimStart('.').ToLowerInvariant();
            var baseName = Path.GetFileNameWithoutExtension(sibling.Filename);

            artifacts.Add(new ModelArtifact
            {
                Name = baseName,
                Format = format,
                FilePaths = new List<string> { sibling.Filename },
                Description = $"{format.ToUpperInvariant()} format model",
                SizeInBytes = 0 // Sibling doesn't have Size property
            });
        }
    }

    private static void AddPlaceholderArtifact(List<Sibling> siblings, List<ModelArtifact> artifacts, string defaultFormat)
    {
        var firstFilename = siblings.FirstOrDefault(s => !string.IsNullOrEmpty(s.Filename))?.Filename;

        if (!string.IsNullOrEmpty(firstFilename))
        {
            var extension = Path.GetExtension(firstFilename);
            var format = !string.IsNullOrEmpty(extension) ? extension.TrimStart('.').ToLowerInvariant() : defaultFormat;
            var baseName = Path.GetFileNameWithoutExtension(firstFilename);

            artifacts.Add(new ModelArtifact
            {
                Name = baseName,
                Format = format,
                FilePaths = new List<string> { firstFilename },
                Description = "Default model file",
                SizeInBytes = 0 // Sibling doesn't have Size property
            });
        }
    }

    private static bool IsModelFileExtension(string extension)
    {
        if (string.IsNullOrEmpty(extension))
            return false;

        extension = extension.ToLowerInvariant();
        return extension == ".gguf" ||
               extension == ".bin" ||
               extension == ".safetensors" ||
               extension == ".ggml" ||
               extension == ".pt" ||
               extension == ".pth";
    }

    // Keep existing method as alias for backward compatibility
    public static async Task<List<ModelArtifact>> ExtractArtifactsFromModelAsync(
        HuggingFaceModel hfModel,
        Dictionary<string, long>? repositoryFileSizes,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        return await ExtractDetailedArtifactsFromModelAsync(hfModel, repositoryFileSizes, logger, cancellationToken);
    }
}