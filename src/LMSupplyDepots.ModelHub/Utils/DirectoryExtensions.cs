namespace LMSupplyDepots.ModelHub.Utils;

/// <summary>
/// Directory extension methods for size calculation
/// </summary>
internal static class DirectoryExtensions
{
    private static readonly string[] ModelFileExtensions = { ".gguf", ".bin", ".safetensors" };

    /// <summary>
    /// Calculates total size of model files in directory
    /// </summary>
    public static long GetModelFilesSize(this string directory)
    {
        if (!Directory.Exists(directory))
            return 0;

        try
        {
            return Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories)
                .Where(f => ModelFileExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .Sum(f => new FileInfo(f).Length);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Gets all model files with their sizes
    /// </summary>
    public static Dictionary<string, long> GetModelFilesWithSizes(this string directory)
    {
        var result = new Dictionary<string, long>();

        if (!Directory.Exists(directory))
            return result;

        try
        {
            foreach (var file in Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ModelFileExtensions.Contains(ext))
                {
                    result[file] = new FileInfo(file).Length;
                }
            }
        }
        catch
        {
            // Return empty dictionary on error
        }

        return result;
    }
}