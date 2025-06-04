namespace LMSupplyDepots.ModelHub.Utils;

/// <summary>
/// Utility methods for creating model instances
/// </summary>
public static class ModelFactory
{
    /// <summary>
    /// Creates a model from a local file
    /// </summary>
    public static LMModel FromLocalFile(string filePath, LMModelCapabilities capabilities)
    {
        string artifactName = Path.GetFileNameWithoutExtension(filePath);
        string format = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();

        return new LMModel
        {
            Id = $"local:{artifactName}",
            Registry = "local",
            RepoId = artifactName,
            Name = artifactName,
            ArtifactName = artifactName,
            Format = format,
            Type = ModelType.TextGeneration,
            Capabilities = capabilities,
            FilePaths = new List<string> { filePath },
            SizeInBytes = new FileInfo(filePath).Length,
            LocalPath = Path.GetDirectoryName(filePath)
        };
    }

    /// <summary>
    /// Creates a model from a collection and artifact
    /// </summary>
    public static LMModel FromCollectionAndArtifact(LMCollection collection, ModelArtifact artifact)
    {
        var model = new LMModel
        {
            Registry = collection.Hub,
            RepoId = collection.CollectionId,
            Name = $"{Path.GetFileName(collection.CollectionId)} ({artifact.Name})",
            Description = artifact.Description,
            Version = collection.Version,
            Capabilities = collection.Capabilities.Clone(),
            ArtifactName = artifact.Name,
            Format = artifact.Format,
            SizeInBytes = artifact.SizeInBytes,
            FilePaths = artifact.FilePaths.ToList(),
            Type = collection.Type
        };

        model.Id = $"{collection.Hub}:{collection.CollectionId}/{artifact.Name}";

        return model;
    }
}