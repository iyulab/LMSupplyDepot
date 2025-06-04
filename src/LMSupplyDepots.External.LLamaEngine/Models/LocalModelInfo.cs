namespace LMSupplyDepots.External.LLamaEngine.Models;

public record LocalModelInfo
{
    public string ModelId { get; set; } = null!;
    public string FullPath { get; set; } = null!;
    public LocalModelState State { get; set; } = LocalModelState.Unloaded;
    public string? LastError { get; set; }

    public static bool TryParseIdentifier(string identifier, out (string provider, string modelName, string fileName) result)
    {
        var parts = identifier.Split(['/', ':'], 3);
        if (parts.Length == 3)
        {
            // Remove .gguf extension if present
            var fileName = parts[2].EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)
                ? parts[2][..^5]  // Remove .gguf string
                : parts[2];

            result = (parts[0], parts[1], fileName);
            return true;
        }

        result = default;
        return false;
    }

    public static LocalModelInfo CreateFromIdentifier(string filePath, string identifier)
    {
        if (!TryParseIdentifier(identifier, out _))
        {
            throw new ArgumentException($"Invalid model identifier format: {identifier}");
        }

        return new LocalModelInfo
        {
            ModelId = identifier,
            FullPath = filePath,
            State = LocalModelState.Unloaded
        };
    }
}

public enum LocalModelState
{
    Unloaded,   // Initial state or unloaded state
    Loading,    // Loading
    Loaded,     // Loading completed
    Failed,     // Loading or operation failed
    Unloading   // Unloading
}