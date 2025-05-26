namespace LMSupplyDepots.ModelHub.Utils;

/// <summary>
/// Helper for managing download status files
/// </summary>
internal static class DownloadStatusHelper
{
    private const string DownloadsDirectory = ".downloads";
    private const string StatusFileExtension = ".download";

    /// <summary>
    /// Creates download status file with total size
    /// </summary>
    public static async Task CreateStatusFileAsync(string sourceId, long totalSize, string basePath)
    {
        var filePath = GetStatusFilePath(sourceId, basePath);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await File.WriteAllTextAsync(filePath, totalSize.ToString());
    }

    /// <summary>
    /// Removes download status file
    /// </summary>
    public static void RemoveStatusFile(string sourceId, string basePath)
    {
        var filePath = GetStatusFilePath(sourceId, basePath);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    /// <summary>
    /// Gets total size from status file
    /// </summary>
    public static long? GetTotalSize(string sourceId, string basePath)
    {
        var filePath = GetStatusFilePath(sourceId, basePath);
        if (!File.Exists(filePath))
        {
            return null;
        }

        return long.TryParse(File.ReadAllText(filePath), out var size) ? size : null;
    }

    /// <summary>
    /// Checks if download is paused
    /// </summary>
    public static bool IsPaused(string sourceId, string basePath)
    {
        return File.Exists(GetStatusFilePath(sourceId, basePath));
    }

    private static string GetStatusFilePath(string sourceId, string basePath)
    {
        var downloadsDir = Path.Combine(basePath, DownloadsDirectory);
        return Path.Combine(downloadsDir, $"{sourceId.ToFileNameSafe()}{StatusFileExtension}");
    }
}