namespace LMSupplyDepots.ModelHub.Exceptions;

/// <summary>
/// Exception thrown when there's insufficient disk space for a model download
/// </summary>
public class InsufficientDiskSpaceException : ModelHubException
{
    /// <summary>
    /// Gets the model size in bytes
    /// </summary>
    public long ModelSizeBytes { get; }

    /// <summary>
    /// Gets the available disk space in bytes
    /// </summary>
    public long AvailableSpaceBytes { get; }

    /// <summary>
    /// Initializes a new instance of the InsufficientDiskSpaceException class
    /// </summary>
    public InsufficientDiskSpaceException(long modelSizeBytes, long availableSpaceBytes)
        : base($"Insufficient disk space to download model. Required: {FormatSize(modelSizeBytes)}, Available: {FormatSize(availableSpaceBytes)}")
    {
        ModelSizeBytes = modelSizeBytes;
        AvailableSpaceBytes = availableSpaceBytes;
    }

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size:0.##} {units[unitIndex]}";
    }
}