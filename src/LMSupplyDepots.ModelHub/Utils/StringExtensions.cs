namespace LMSupplyDepots.ModelHub.Utils;

/// <summary>
/// String extension methods for download management
/// </summary>
internal static class StringExtensions
{
    /// <summary>
    /// Converts source ID to safe file name
    /// </summary>
    public static string ToFileNameSafe(this string input)
    {
        if (string.IsNullOrEmpty(input))
            return "unknown";

        var invalidChars = Path.GetInvalidFileNameChars();
        return new string(input.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
    }

    /// <summary>
    /// Extracts file name from source ID
    /// </summary>
    public static string GetFileNameFromSourceId(this string sourceId)
    {
        if (sourceId.Contains('/'))
        {
            return sourceId.Split('/').Last();
        }
        return sourceId;
    }
}