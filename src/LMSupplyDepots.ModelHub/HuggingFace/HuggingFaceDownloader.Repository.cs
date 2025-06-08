using LMSupplyDepots.External.HuggingFace.Models;

namespace LMSupplyDepots.ModelHub.HuggingFace;

/// <summary>
/// Implementation of collection information operations with optimized discovery vs detailed info
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
            collectionId, collection.Artifacts.Count);

        if (collection.Artifacts.Count > 0)
        {
            _logger.LogInformation("Available artifacts in {CollectionId}: {Artifacts}",
                collectionId, string.Join(", ", collection.Artifacts.Select(a => a.Name)));
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

            var artifactList = collection.Artifacts;
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

            if (collection.Artifacts.Count == 0)
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

                collection.Artifacts.Add(placeholder);
                model = Utils.ModelFactory.FromCollectionAndArtifact(collection, placeholder);
                return model;
            }

            var availableArtifacts = string.Join(", ", collection.Artifacts.Select(a => a.Name));
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
    /// Gets information about a model collection with actual file sizes (for detailed info)
    /// </summary>
    public async Task<LMCollection> GetCollectionInfoAsync(string collectionId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting detailed collection information for {CollectionId}", collectionId);

        try
        {
            var hfModel = await _client.Value.FindModelByRepoIdAsync(collectionId, cancellationToken);

            // Get actual file sizes from repository for detailed info
            Dictionary<string, long>? repositoryFileSizes = null;
            try
            {
                repositoryFileSizes = await _client.Value.GetRepositoryFileSizesAsync(collectionId, cancellationToken);
                _logger.LogInformation("Retrieved actual file sizes for {Count} files in {CollectionId}",
                    repositoryFileSizes.Count, collectionId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get repository file sizes for {CollectionId}, will proceed without sizes", collectionId);
            }

            // Create detailed collection with file sizes
            var collection = await HuggingFaceHelper.CreateDetailedCollectionAsync(
                hfModel, repositoryFileSizes, _logger, cancellationToken);

            _logger.LogInformation("Created detailed collection {CollectionId} with {Count} artifacts",
                collectionId, collection.Artifacts.Count);

            return collection;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting collection information for {CollectionId}", collectionId);
            throw new ModelHubException($"Failed to get collection information for '{collectionId}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Discovers model collections in Hugging Face with fast loading (no file sizes)
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

            // Process models without file size information for fast discovery
            foreach (var hfModel in results)
            {
                try
                {
                    // Create lightweight collection without file sizes
                    var collection = HuggingFaceHelper.CreateLightweightCollection(hfModel);
                    collections.Add(collection);

                    _logger.LogDebug("Created lightweight collection {CollectionId} with {Count} artifacts",
                        collection.CollectionId, collection.Artifacts.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to convert model {ModelId} to lightweight collection", hfModel.ModelId);
                }
            }

            _logger.LogInformation("Discovered {Count} collections in fast mode", collections.Count);
            return collections;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering collections");
            throw new ModelHubException($"Failed to discover collections: {ex.Message}", ex);
        }
    }
}