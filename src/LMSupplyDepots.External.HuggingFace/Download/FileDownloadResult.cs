namespace LMSupplyDepots.External.HuggingFace.Download;

public class FileDownloadResult
{
    public required string FilePath { get; init; }
    public required long BytesDownloaded { get; init; }
    public required long? TotalBytes { get; init; }
    public required bool IsCompleted { get; init; }
}