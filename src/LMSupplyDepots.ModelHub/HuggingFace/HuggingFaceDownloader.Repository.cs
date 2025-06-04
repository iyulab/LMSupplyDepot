using LMSupplyDepots.External.HuggingFace.Models;

namespace LMSupplyDepots.ModelHub.HuggingFace;

/// <summary>
/// Implementation of collection information operations with accurate size handling
/// </summary>
public partial class HuggingFaceDownloader
{
    /// <summary>
    /// Gets information about a model from Hugging Face without downloading it
    /// </summary>
    public async Task<LMModel> GetModelInfoAsync(string modelId, CancellationToken cancellationToken = default)
    {
        var (collectionId, artifactName) = HuggingFaceHelper.NormalizeAndSplitSourceId(modelId);

        var collection = await GetCollectionInfoAsync(collectionId, cancellationToken);

        _logger.LogInformation("Collection {CollectionId} has {Count} available artifacts",
            collectionId, collection.AvailableArtifacts.Count);

        if (collection.AvailableArtifacts.Count > 0)
        {
            _logger.LogInformation("Available artifacts in {CollectionId}: {Artifacts}",
                collectionId, string.Join(", ", collection.AvailableArtifacts.Select(a => a.Name)));
        }

        if (!string.IsNullOrEmpty(artifactName))
        {
            _logger.LogInformation("Looking for artifact '{ArtifactName}' in collection '{CollectionId}'",
                artifactName, collectionId);

            var model = collection.GetModel(artifactName);
            if (model != null)
            {
                _logger.LogInformation("Found exact match for artifact '{ArtifactName}'", artifactName);
                return model;
            }

            var artifactList = collection.AvailableArtifacts;
            var matchingArtifact = artifactList.FirstOrDefault(a =>
                a.Name.Equals(artifactName, StringComparison.OrdinalIgnoreCase));

            if (matchingArtifact != null)
            {
                _logger.LogInformation("Found case-insensitive match for artifact '{ArtifactName}': '{MatchingName}'",
                    artifactName, matchingArtifact.Name);
                model = Utils.ModelFactory.FromCollectionAndArtifact(collection, matchingArtifact);
                return model;
            }

            var partialMatch = artifactList.FirstOrDefault(a =>
                a.Name.Contains(artifactName, StringComparison.OrdinalIgnoreCase) ||
                artifactName.Contains(a.Name, StringComparison.OrdinalIgnoreCase));

            if (partialMatch != null)
            {
                _logger.LogInformation("Found partial match for artifact '{ArtifactName}': '{MatchingName}'",
                    artifactName, partialMatch.Name);
                model = Utils.ModelFactory.FromCollectionAndArtifact(collection, partialMatch);
                return model;
            }

            var mostSimilarArtifact = FindMostSimilarArtifact(artifactList, artifactName);
            if (mostSimilarArtifact != null)
            {
                _logger.LogInformation("Found similar artifact '{SimilarName}' for requested '{ArtifactName}'",
                    mostSimilarArtifact.Name, artifactName);
                model = Utils.ModelFactory.FromCollectionAndArtifact(collection, mostSimilarArtifact);
                return model;
            }

            if (collection.AvailableArtifacts.Count == 0)
            {
                _logger.LogWarning("No artifacts found in collection '{CollectionId}', creating placeholder for '{ArtifactName}'",
                    collectionId, artifactName);

                var placeholder = new ModelArtifact
                {
                    Name = artifactName,
                    Format = collection.DefaultFormat,
                    Description = $"Placeholder for {artifactName}",
                    SizeInBytes = 0,
                    FilePaths = new List<string>()
                };

                collection.AvailableArtifacts.Add(placeholder);
                model = Utils.ModelFactory.FromCollectionAndArtifact(collection, placeholder);
                return model;
            }

            var availableArtifacts = string.Join(", ", collection.AvailableArtifacts.Select(a => a.Name));
            throw new ModelNotFoundException(
                artifactName,
                $"Artifact '{artifactName}' not found in collection '{collectionId}'. Available artifacts: {availableArtifacts}");
        }

        var recommendedModel = collection.GetRecommendedModel();
        if (recommendedModel != null)
        {
            _logger.LogInformation("Using recommended model from collection '{CollectionId}'", collectionId);
            return recommendedModel;
        }

        throw new ModelNotFoundException(collectionId, $"No models found in collection '{collectionId}'");
    }

    /// <summary>
    /// Finds the most similar artifact to the requested name
    /// </summary>
    private ModelArtifact? FindMostSimilarArtifact(List<ModelArtifact> artifacts, string requestedName)
    {
        if (artifacts == null || artifacts.Count == 0 || string.IsNullOrEmpty(requestedName))
            return null;

        var containsArtifact = artifacts.FirstOrDefault(a =>
            a.Name.Contains(requestedName, StringComparison.OrdinalIgnoreCase) ||
            requestedName.Contains(a.Name, StringComparison.OrdinalIgnoreCase));

        if (containsArtifact != null)
            return containsArtifact;

        var normalizedRequest = requestedName.ToLowerInvariant()
            .Replace("-", "_")
            .Replace(".", "_");

        foreach (var artifact in artifacts)
        {
            var normalizedName = artifact.Name.ToLowerInvariant()
                .Replace("-", "_")
                .Replace(".", "_");

            var minLength = Math.Min(normalizedName.Length, normalizedRequest.Length);
            int matchingPrefixLength = 0;

            for (int i = 0; i < minLength; i++)
            {
                if (normalizedName[i] == normalizedRequest[i])
                    matchingPrefixLength++;
                else
                    break;
            }

            if (matchingPrefixLength > minLength / 2)
                return artifact;
        }

        var requestedFormat = "";
        if (requestedName.Contains('.'))
        {
            requestedFormat = Path.GetExtension(requestedName).TrimStart('.').ToLowerInvariant();
        }

        if (!string.IsNullOrEmpty(requestedFormat))
        {
            var formatMatch = artifacts.FirstOrDefault(a => a.Format.Equals(requestedFormat, StringComparison.OrdinalIgnoreCase));
            if (formatMatch != null)
                return formatMatch;
        }

        return null;
    }

    /// <summary>
    /// Gets information about a model collection with actual file sizes
    /// </summary>
    public async Task<LMCollection> GetCollectionInfoAsync(string collectionId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting collection information for {CollectionId}", collectionId);

        try
        {
            var hfModel = await _client.Value.FindModelByRepoIdAsync(collectionId, cancellationToken);

            var collection = new LMCollection
            {
                Id = $"hf:{collectionId}",
                Hub = "hf",
                CollectionId = collectionId,
                Name = string.IsNullOrEmpty(collectionId) ? "Unknown Model" : Path.GetFileName(collectionId),
                Type = HuggingFaceHelper.DetermineModelType(hfModel),
                DefaultFormat = HuggingFaceHelper.GetModelFormat(hfModel),
                Version = hfModel.LastModified.ToString("yyyyMMdd"),
                Description = HuggingFaceHelper.GetModelDescription(hfModel),
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
                    SupportsTextGeneration = HuggingFaceHelper.DetermineModelType(hfModel) == ModelType.TextGeneration,
                    SupportsEmbeddings = HuggingFaceHelper.DetermineModelType(hfModel) == ModelType.Embedding,
                    SupportsImageUnderstanding = hfModel.Tags.Any(t =>
                        t.Contains("vision") || t.Contains("image") || t.Contains("multimodal")),
                    MaxContextLength = HuggingFaceHelper.GetMaxContextLength(hfModel),
                    EmbeddingDimension = HuggingFaceHelper.GetEmbeddingDimension(hfModel)
                }
            };

            // Get actual file sizes from repository
            Dictionary<string, long>? repositoryFileSizes = null;
            try
            {
                repositoryFileSizes = await _client.Value.GetRepositoryFileSizesAsync(collectionId, cancellationToken);
                _logger.LogInformation("Retrieved actual file sizes for {Count} files in {CollectionId}",
                    repositoryFileSizes.Count, collectionId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get repository file sizes for {CollectionId}, will use sibling info", collectionId);
            }

            List<string> files = new List<string>();
            try
            {
                var fileInfos = await _client.Value.GetRepositoryFilesAsync(collectionId, null, cancellationToken);

                foreach (var fileInfo in fileInfos)
                {
                    if (fileInfo.IsFile)
                    {
                        files.Add(fileInfo.Path);
                    }
                }

                _logger.LogInformation("Retrieved {Count} files from collection {CollectionId} using API",
                    files.Count, collectionId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get collection files for {CollectionId} using API. Will try using siblings info.", collectionId);
                files = new List<string>();
            }

            if (files.Count == 0 && hfModel.Siblings != null && hfModel.Siblings.Count > 0)
            {
                _logger.LogInformation("Using siblings information to extract files for {CollectionId}", collectionId);

                foreach (var sibling in hfModel.Siblings)
                {
                    if (!string.IsNullOrEmpty(sibling.Filename))
                    {
                        files.Add(sibling.Filename);
                    }
                }

                _logger.LogInformation("Retrieved {Count} files from siblings info for {CollectionId}",
                    files.Count, collectionId);
            }

            files = files.Where(f => !string.IsNullOrWhiteSpace(f)).ToList();

            // Extract artifacts with actual file sizes
            collection.AvailableArtifacts = await HuggingFaceHelper.ExtractArtifactsAsync(
                files, collection.DefaultFormat, repositoryFileSizes);

            _logger.LogInformation("Extracted {Count} artifacts from {Files} files for {CollectionId}",
                collection.AvailableArtifacts.Count, files.Count, collectionId);

            if (collection.AvailableArtifacts.Count == 0 && hfModel.Siblings != null && hfModel.Siblings.Count > 0)
            {
                _logger.LogInformation("Creating artifacts directly from siblings for {CollectionId}", collectionId);
                collection.AvailableArtifacts = HuggingFaceHelper.ExtractArtifactsFromSiblings(hfModel.Siblings, collection.DefaultFormat);

                _logger.LogInformation("Created {Count} artifacts directly from siblings for {CollectionId}",
                    collection.AvailableArtifacts.Count, collectionId);
            }

            if (collection.AvailableArtifacts.Count == 0)
            {
                _logger.LogWarning("No artifacts found for {CollectionId}, creating placeholder artifact", collectionId);
                collection.AvailableArtifacts.Add(new ModelArtifact
                {
                    Name = Path.GetFileName(collectionId),
                    Format = collection.DefaultFormat,
                    Description = $"Default {collection.DefaultFormat} model",
                    SizeInBytes = 0,
                    FilePaths = new List<string>()
                });
            }

            return collection;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting collection information for {CollectionId}", collectionId);
            throw new ModelHubException($"Failed to get collection information for '{collectionId}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Discovers model collections in Hugging Face with accurate size information
    /// </summary>
    public async Task<IReadOnlyList<LMCollection>> DiscoverCollectionsAsync(
        ModelType? type = null,
        string? searchTerm = null,
        int limit = 10,
        ModelSortField sort = ModelSortField.Downloads,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Discovering collections with term: {SearchTerm}, type: {Type}, limit: {Limit}",
            searchTerm, type, limit);

        try
        {
            IReadOnlyList<HuggingFaceModel> results;

            if (type == ModelType.TextGeneration)
            {
                results = await _client.Value.SearchTextGenerationModelsAsync(
                    searchTerm, null, limit, sort, true, cancellationToken);
            }
            else if (type == ModelType.Embedding)
            {
                results = await _client.Value.SearchEmbeddingModelsAsync(
                    searchTerm, null, limit, sort, true, cancellationToken);
            }
            else
            {
                var textGenResults = await _client.Value.SearchTextGenerationModelsAsync(
                    searchTerm, null, limit / 2, sort, true, cancellationToken);

                var embeddingResults = await _client.Value.SearchEmbeddingModelsAsync(
                    searchTerm, null, limit / 2, sort, true, cancellationToken);

                results = [.. textGenResults.Concat(embeddingResults)
                .OrderByDescending(m => m.Downloads)
                .Take(limit)];
            }

            var collections = new List<LMCollection>();
            foreach (var hfModel in results)
            {
                try
                {
                    var collectionId = hfModel.ModelId;
                    var modelType = HuggingFaceHelper.DetermineModelType(hfModel);
                    var format = HuggingFaceHelper.GetModelFormat(hfModel);

                    var collection = new LMCollection
                    {
                        Id = $"hf:{collectionId}",
                        Hub = "hf",
                        CollectionId = collectionId,
                        Name = string.IsNullOrEmpty(collectionId) ?
                            "Unknown Model" : Path.GetFileName(collectionId),
                        Type = modelType,
                        DefaultFormat = format,
                        Version = hfModel.LastModified.ToString("yyyyMMdd"),
                        Description = HuggingFaceHelper.GetModelDescription(hfModel),
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
                            MaxContextLength = HuggingFaceHelper.GetMaxContextLength(hfModel),
                            EmbeddingDimension = HuggingFaceHelper.GetEmbeddingDimension(hfModel)
                        }
                    };

                    PopulateCollectionArtifactsWithActualSizes(collection, hfModel);

                    collections.Add(collection);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to convert model {ModelId} to collection", hfModel.ModelId);
                }
            }

            return collections;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering collections");
            throw new ModelHubException($"Failed to discover collections: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Populates collection artifacts with actual file sizes from siblings
    /// </summary>
    private static void PopulateCollectionArtifactsWithActualSizes(LMCollection collection, HuggingFaceModel hfModel)
    {
        if (hfModel.Siblings != null && hfModel.Siblings.Any())
        {
            var modelFiles = hfModel.Siblings
                .Where(s => !string.IsNullOrEmpty(s.Filename) &&
                          (s.Filename.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) ||
                           s.Filename.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase) ||
                           s.Filename.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (modelFiles.Any())
            {
                foreach (var file in modelFiles)
                {
                    var artifactName = Path.GetFileNameWithoutExtension(file.Filename);
                    var artifactFormat = Path.GetExtension(file.Filename).TrimStart('.');

                    int? quantBits = null;
                    string? sizeCategory = null;

                    var quantMatch = System.Text.RegularExpressions.Regex.Match(
                        artifactName, @"Q(\d+)(_[KM])?(_S|_M|_L|_XL)?");
                    if (quantMatch.Success)
                    {
                        quantBits = int.Parse(quantMatch.Groups[1].Value);
                    }

                    var sizeMatch = System.Text.RegularExpressions.Regex.Match(
                        artifactName, @"(_XS|_S|_M|_L|_XL|-xs|-small|-medium|-large|-xl)$");
                    if (sizeMatch.Success)
                    {
                        sizeCategory = GetNormalizedSizeCategory(sizeMatch.Groups[1].Value);
                    }

                    var artifact = new ModelArtifact
                    {
                        Name = artifactName,
                        Format = artifactFormat,
                        Description = GetArtifactDescription(artifactName, artifactFormat),
                        SizeInBytes = 0,
                        FilePaths = new List<string> { file.Filename },
                        QuantizationBits = quantBits,
                        SizeCategory = sizeCategory
                    };

                    collection.AvailableArtifacts.Add(artifact);
                }
            }
            else
            {
                AddPlaceholderArtifact(collection, hfModel);
            }
        }
        else
        {
            AddPlaceholderArtifact(collection, hfModel);
        }
    }

    /// <summary>
    /// Adds a placeholder artifact when no specific artifacts are found
    /// </summary>
    private static void AddPlaceholderArtifact(LMCollection collection, HuggingFaceModel hfModel)
    {
        var defaultFormat = HuggingFaceHelper.GetModelFormat(hfModel);
        var artifactName = hfModel.ModelId.Split('/').Last();

        var placeholderArtifact = new ModelArtifact
        {
            Name = artifactName,
            Format = defaultFormat,
            Description = $"Default {defaultFormat} model in this collection",
            SizeInBytes = 0,
            FilePaths = new List<string>()
        };

        collection.AvailableArtifacts.Add(placeholderArtifact);
    }

    /// <summary>
    /// Generates a description for an artifact based on its name and format
    /// </summary>
    private static string GetArtifactDescription(string artifactName, string format)
    {
        var parts = new List<string>();

        parts.Add($"{format.ToUpperInvariant()} format");

        if (artifactName.Contains("Q2") || artifactName.Contains("Q3") ||
            artifactName.Contains("Q4") || artifactName.Contains("Q5") ||
            artifactName.Contains("Q6") || artifactName.Contains("Q8"))
        {
            var quantMatch = System.Text.RegularExpressions.Regex.Match(
                artifactName, @"Q(\d+)(_[KM])?(_S|_M|_L|_XL)?");
            if (quantMatch.Success)
            {
                parts.Add($"Q{quantMatch.Groups[1].Value} quantization");
            }
        }

        var sizeMatch = System.Text.RegularExpressions.Regex.Match(
            artifactName, @"(_XS|_S|_M|_L|_XL|-xs|-small|-medium|-large|-xl)$");
        if (sizeMatch.Success)
        {
            var sizeCategory = GetNormalizedSizeCategory(sizeMatch.Groups[1].Value);
            parts.Add($"{sizeCategory} size");
        }

        return string.Join(", ", parts);
    }

    /// <summary>
    /// Normalizes size category string to standard form
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
}