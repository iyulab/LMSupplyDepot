namespace LMSupplyDepots.ModelHub.Models;

/// <summary>
/// Represents a model collection containing multiple artifacts
/// </summary>
public class LMCollection
{
    /// <summary>
    /// Collection identifier
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Model hub (e.g., "hf", "local")
    /// </summary>
    public string Hub { get; set; } = string.Empty;

    /// <summary>
    /// Collection ID within the hub
    /// </summary>
    public string CollectionId { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the collection
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The type of models in this collection
    /// </summary>
    public ModelType Type { get; set; }

    /// <summary>
    /// Default format of models in this collection
    /// </summary>
    public string DefaultFormat { get; set; } = string.Empty;

    /// <summary>
    /// Version information for the collection
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Brief description of the collection
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Publisher or owner of the collection
    /// </summary>
    public string Publisher { get; set; } = string.Empty;

    /// <summary>
    /// Available artifacts in this model collection
    /// </summary>
    public List<ModelArtifact> Artifacts { get; set; } = new();

    /// <summary>
    /// Common capabilities for all models in this collection
    /// </summary>
    public LMModelCapabilities Capabilities { get; set; } = new();

    /// <summary>
    /// Tags associated with the model collection
    /// </summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// Number of downloads
    /// </summary>
    public long Downloads { get; set; }

    /// <summary>
    /// Number of likes/hearts
    /// </summary>
    public long Likes { get; set; }

    /// <summary>
    /// Creation date
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Last modified date
    /// </summary>
    public DateTime LastModified { get; set; }

    /// <summary>
    /// Whether this model requires authentication/gating
    /// </summary>
    public bool IsGated { get; set; }

    /// <summary>
    /// License information if available
    /// </summary>
    public string? License { get; set; }

    /// <summary>
    /// Language support (e.g., "multilingual", "english", etc.)
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Get a specific model from this collection by artifact name
    /// </summary>
    public LMModel? GetModel(string artifactName)
    {
        var artifact = Artifacts.FirstOrDefault(a => a.Name == artifactName);
        if (artifact == null)
            return null;

        return Utils.ModelFactory.FromCollectionAndArtifact(this, artifact);
    }

    /// <summary>
    /// Get all models from this collection
    /// </summary>
    public IReadOnlyList<LMModel> GetAllModels()
    {
        return Artifacts
            .Select(a => Utils.ModelFactory.FromCollectionAndArtifact(this, a))
            .ToList();
    }

    /// <summary>
    /// Gets the recommended model from this collection
    /// </summary>
    public LMModel? GetRecommendedModel()
    {
        if (Artifacts.Count == 0)
            return null;

        var mediumSizedArtifact = Artifacts.FirstOrDefault(a =>
            a.SizeCategory == "M" ||
            a.Name.Contains("Q5_K_M") ||
            a.Name.Contains("Q4_K_M") ||
            a.Name.Contains("_M") ||
            a.Name.Contains("-medium"));

        if (mediumSizedArtifact != null)
            return Utils.ModelFactory.FromCollectionAndArtifact(this, mediumSizedArtifact);

        var q5Artifact = Artifacts.FirstOrDefault(a =>
            a.QuantizationBits == 5 ||
            a.Name.Contains("Q5"));

        if (q5Artifact != null)
            return Utils.ModelFactory.FromCollectionAndArtifact(this, q5Artifact);

        var q4Artifact = Artifacts.FirstOrDefault(a =>
            a.QuantizationBits == 4 ||
            a.Name.Contains("Q4"));

        if (q4Artifact != null)
            return Utils.ModelFactory.FromCollectionAndArtifact(this, q4Artifact);

        return Utils.ModelFactory.FromCollectionAndArtifact(this, Artifacts[0]);
    }

    /// <summary>
    /// Creates a copy of this collection
    /// </summary>
    public LMCollection Clone()
    {
        return new LMCollection
        {
            Id = Id,
            Hub = Hub,
            CollectionId = CollectionId,
            Name = Name,
            Type = Type,
            DefaultFormat = DefaultFormat,
            Version = Version,
            Description = Description,
            Publisher = Publisher,
            Capabilities = Capabilities.Clone(),
            Artifacts = Artifacts.Select(a => a.Clone()).ToList(),
            Tags = new List<string>(Tags),
            Downloads = Downloads,
            Likes = Likes,
            CreatedAt = CreatedAt,
            LastModified = LastModified,
            IsGated = IsGated,
            License = License,
            Language = Language
        };
    }
}