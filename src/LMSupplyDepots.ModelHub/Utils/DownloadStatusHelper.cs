namespace LMSupplyDepots.ModelHub.Utils;

/// <summary>
/// Helper class for handling download status operations
/// </summary>
public static class DownloadStatusHelper
{
    /// <summary>
    /// Creates detailed download progress from basic status and progress information
    /// </summary>
    public static async Task<ModelDownloadProgress?> CreateDetailedProgressAsync(
        string modelId,
        ModelDownloadStatus? status,
        ModelDownloadProgress? progress,
        IModelRepository repository,
        IEnumerable<DownloadInfo> allDownloads,
        CancellationToken cancellationToken = default)
    {
        var downloadInfo = allDownloads.FirstOrDefault(d => d.ModelId == modelId);
        var startedAt = downloadInfo?.StartedAt;

        // If no status, check if model is already downloaded
        if (status == null)
        {
            var isDownloaded = await repository.ExistsAsync(modelId, cancellationToken);
            if (isDownloaded)
            {
                var model = await repository.GetModelAsync(modelId, cancellationToken);
                if (model != null)
                {
                    return ModelDownloadProgress.CreateCompleted(
                        modelId,
                        GetMainFileName(model),
                        model.SizeInBytes,
                        startedAt);
                }
            }

            return ModelDownloadProgress.CreateNotFound(modelId);
        }

        // If we have progress, create from progress with start time
        if (progress != null)
        {
            return new ModelDownloadProgress
            {
                ModelId = progress.ModelId,
                FileName = progress.FileName,
                BytesDownloaded = progress.BytesDownloaded,
                TotalBytes = progress.TotalBytes,
                BytesPerSecond = progress.BytesPerSecond,
                EstimatedTimeRemaining = progress.EstimatedTimeRemaining,
                Status = progress.Status,
                ErrorMessage = progress.ErrorMessage,
                StartedAt = startedAt ?? progress.StartedAt
            };
        }

        // Create basic status without progress
        return new ModelDownloadProgress
        {
            ModelId = modelId,
            FileName = "",
            BytesDownloaded = 0,
            TotalBytes = null,
            BytesPerSecond = 0,
            Status = status.Value,
            StartedAt = startedAt
        };
    }

    /// <summary>
    /// Gets the main file name from a model
    /// </summary>
    private static string GetMainFileName(LMModel model)
    {
        if (model.FilePaths.Count > 0)
            return model.FilePaths[0];

        if (!string.IsNullOrEmpty(model.ArtifactName))
            return $"{model.ArtifactName}.{model.Format}";

        return model.Name;
    }
}