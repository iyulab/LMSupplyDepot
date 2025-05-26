namespace LMSupplyDepots.ModelHub.Models;

/// <summary>
/// Value object representing a model identifier with immutable properties
/// </summary>
public readonly struct ModelIdentifier : IEquatable<ModelIdentifier>
{
    /// <summary>
    /// Registry of the model (e.g., "huggingface", "local")
    /// </summary>
    public string Registry { get; }

    /// <summary>
    /// Publisher of the model (e.g., "meta", "mistral")
    /// </summary>
    public string Publisher { get; }

    /// <summary>
    /// Name of the model (e.g., "Llama-3-8B-Instruct")
    /// </summary>
    public string ModelName { get; }

    /// <summary>
    /// Specific artifact name (e.g., "Llama-3-8B-Instruct-Q4_K_M")
    /// </summary>
    public string ArtifactName { get; }

    /// <summary>
    /// File format of the model (e.g., "gguf", "safetensors")
    /// </summary>
    public string Format { get; }

    /// <summary>
    /// For backwards compatibility - collection ID is publisher/modelName
    /// </summary>
    public string CollectionId => $"{Publisher}/{ModelName}";

    /// <summary>
    /// For backwards compatibility
    /// </summary>
    public string RepoId => CollectionId;

    /// <summary>
    /// Creates a new immutable model identifier
    /// </summary>
    public ModelIdentifier(
        string registry,
        string publisher,
        string modelName,
        string artifactName,
        string format = "gguf")
    {
        Registry = registry ?? throw new ArgumentNullException(nameof(registry));
        Publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        ModelName = modelName ?? throw new ArgumentNullException(nameof(modelName));
        ArtifactName = artifactName ?? throw new ArgumentNullException(nameof(artifactName));
        Format = format ?? "gguf";
    }

    /// <summary>
    /// Parses a model ID string into a ModelIdentifier
    /// </summary>
    public static ModelIdentifier Parse(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            throw new ArgumentException("Model ID cannot be empty", nameof(modelId));
        }

        // Handle full format: registry:publisher/modelName/artifactName
        var registrySplit = modelId.Split(new[] { ':' }, 2);

        string registry;
        string remaining;

        if (registrySplit.Length == 2)
        {
            registry = registrySplit[0];
            remaining = registrySplit[1];
        }
        else
        {
            registry = "local";
            remaining = modelId;
        }

        // Handle the rest of the path parts
        var pathParts = remaining.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

        string publisher;
        string modelName;
        string artifactName;
        string format = "gguf";

        if (pathParts.Length == 1)
        {
            // Just one part - treat it as both publisher and model name
            publisher = "local";
            modelName = pathParts[0];
            artifactName = modelName;
        }
        else if (pathParts.Length == 2)
        {
            // publisher/model - a standard format
            publisher = pathParts[0];
            modelName = pathParts[1];
            artifactName = modelName;
        }
        else if (pathParts.Length >= 3)
        {
            // publisher/model/artifact - a full format
            publisher = pathParts[0];
            modelName = pathParts[1];

            // Join all remaining parts as artifact name (in case artifact name contains '/')
            artifactName = string.Join("/", pathParts.Skip(2));
        }
        else
        {
            throw new ArgumentException($"Invalid model ID format: {modelId}", nameof(modelId));
        }

        // Try to detect format from extension in artifact name
        if (artifactName.Contains('.'))
        {
            var extension = Path.GetExtension(artifactName);
            if (!string.IsNullOrEmpty(extension))
            {
                format = extension.TrimStart('.').ToLowerInvariant();
                // Remove extension from artifactName
                artifactName = Path.GetFileNameWithoutExtension(artifactName);
            }
        }

        return new ModelIdentifier(registry, publisher, modelName, artifactName, format);
    }

    /// <summary>
    /// Tries to parse a model ID string into a ModelIdentifier
    /// </summary>
    public static bool TryParse(string modelId, out ModelIdentifier identifier)
    {
        try
        {
            identifier = Parse(modelId);
            return true;
        }
        catch
        {
            identifier = default;
            return false;
        }
    }

    /// <summary>
    /// Creates a local model identifier
    /// </summary>
    public static ModelIdentifier CreateLocal(string name, string format = "gguf")
    {
        return new ModelIdentifier("local", "local", name, name, format);
    }

    /// <summary>
    /// Creates a HuggingFace model identifier
    /// </summary>
    public static ModelIdentifier CreateHuggingFace(
        string publisher,
        string modelName,
        string artifactName,
        string format = "gguf")
    {
        return new ModelIdentifier("hf", publisher, modelName, artifactName, format);
    }

    /// <summary>
    /// Converts an LMModel to a ModelIdentifier
    /// </summary>
    public static ModelIdentifier FromLMModel(LMModel model)
    {
        if (model == null)
            throw new ArgumentNullException(nameof(model));

        // Handle missing or incomplete fields by using defaults or inferring from other fields
        string registry = !string.IsNullOrEmpty(model.Registry) ? model.Registry : "local";

        string publisher;
        string modelName;

        // Parse RepoId into publisher and modelName
        if (!string.IsNullOrEmpty(model.RepoId) && model.RepoId.Contains('/'))
        {
            var repoParts = model.RepoId.Split('/');
            publisher = repoParts[0];
            modelName = string.Join("/", repoParts.Skip(1));
        }
        else
        {
            publisher = "local";
            modelName = !string.IsNullOrEmpty(model.RepoId) ? model.RepoId : model.Name;
        }

        string artifactName = !string.IsNullOrEmpty(model.ArtifactName) ? model.ArtifactName : model.Name;
        string format = !string.IsNullOrEmpty(model.Format) ? model.Format : "gguf";

        return new ModelIdentifier(registry, publisher, modelName, artifactName, format);
    }

    /// <summary>
    /// Updates an LMModel with information from this ModelIdentifier
    /// </summary>
    public void UpdateLMModel(LMModel model)
    {
        model.Id = ToString();
        model.Registry = Registry;
        model.RepoId = CollectionId;
        model.ArtifactName = ArtifactName;
        model.Format = Format;
    }

    /// <summary>
    /// Returns the fully qualified ID of this model
    /// </summary>
    public override string ToString()
    {
        return $"{Registry}:{Publisher}/{ModelName}/{ArtifactName}";
    }

    /// <summary>
    /// Creates a new ModelIdentifier with updated artifact name
    /// </summary>
    public ModelIdentifier WithArtifactName(string newArtifactName)
    {
        return new ModelIdentifier(Registry, Publisher, ModelName, newArtifactName, Format);
    }

    /// <summary>
    /// Creates a new ModelIdentifier with updated format
    /// </summary>
    public ModelIdentifier WithFormat(string newFormat)
    {
        return new ModelIdentifier(Registry, Publisher, ModelName, ArtifactName, newFormat);
    }

    /// <summary>
    /// Determines if this ModelIdentifier is equal to another object
    /// </summary>
    public override bool Equals(object? obj)
    {
        return obj is ModelIdentifier identifier && Equals(identifier);
    }

    /// <summary>
    /// Determines if this ModelIdentifier is equal to another ModelIdentifier
    /// </summary>
    public bool Equals(ModelIdentifier other)
    {
        return Registry == other.Registry &&
               Publisher == other.Publisher &&
               ModelName == other.ModelName &&
               ArtifactName == other.ArtifactName &&
               Format == other.Format;
    }

    /// <summary>
    /// Gets the hash code for this ModelIdentifier
    /// </summary>
    public override int GetHashCode()
    {
        return HashCode.Combine(Registry, Publisher, ModelName, ArtifactName, Format);
    }

    /// <summary>
    /// Equality operator
    /// </summary>
    public static bool operator ==(ModelIdentifier left, ModelIdentifier right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Inequality operator
    /// </summary>
    public static bool operator !=(ModelIdentifier left, ModelIdentifier right)
    {
        return !(left == right);
    }
}