using LMSupplyDepots.External.HuggingFace.Models;

namespace LMSupplyDepots.ModelHub.Models;

/// <summary>
/// Extended model representation for ModelHub with collection and artifact information
/// </summary>
public class HubModel
{
    /// <summary>
    /// The core model information
    /// </summary>
    public LMModel Model { get; set; } = new();

    /// <summary>
    /// Reference to the collection this model belongs to
    /// </summary>
    public LMCollection Collection { get; set; }

    /// <summary>
    /// Model artifact specification this model represents
    /// </summary>
    public ModelArtifact Artifact { get; set; }

    /// <summary>
    /// Creates a new HubModel instance
    /// </summary>
    public HubModel(LMModel model, LMCollection collection, ModelArtifact artifact)
    {
        Model = model;
        Collection = collection;
        Artifact = artifact;
    }

    /// <summary>
    /// Creates a HubModel from a collection and artifact
    /// </summary>
    public static HubModel FromCollectionAndArtifact(LMCollection collection, ModelArtifact artifact)
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

        // Generate the full ID in the format registry:collectionId/artifactName
        model.Id = $"{collection.Hub}:{collection.CollectionId}/{artifact.Name}";

        return new HubModel(model, collection, artifact);
    }

    /// <summary>
    /// Converts to a standard LMModel
    /// </summary>
    public LMModel ToLMModel()
    {
        // Ensure the model has all the necessary properties updated from Collection and Artifact
        Model.Registry = Collection.Hub;
        Model.RepoId = Collection.CollectionId;
        Model.Type = Collection.Type;
        Model.Format = Artifact.Format;
        Model.ArtifactName = Artifact.Name;
        Model.SizeInBytes = Artifact.SizeInBytes;
        Model.FilePaths = Artifact.FilePaths.ToList();

        // Ensure ID is in the correct format
        if (string.IsNullOrEmpty(Model.Id) || !Model.Id.Contains(':') || !Model.Id.Contains('/'))
        {
            Model.Id = $"{Collection.Hub}:{Collection.CollectionId}/{Artifact.Name}";
        }

        return Model;
    }
}