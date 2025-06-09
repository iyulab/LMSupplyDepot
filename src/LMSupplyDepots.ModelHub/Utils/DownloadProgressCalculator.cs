namespace LMSupplyDepots.ModelHub.Utils;

/// <summary>
/// Handles download progress calculations and state transitions
/// </summary>
public static class DownloadProgressCalculator
{
    /// <summary>
    /// Creates detailed download progress from session and repository state
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

        // Priority 1: Use existing progress if available (contains real-time data)
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

        // Priority 2: Use download info progress if available
        if (downloadInfo?.Progress != null)
        {
            return new ModelDownloadProgress
            {
                ModelId = downloadInfo.Progress.ModelId,
                FileName = downloadInfo.Progress.FileName,
                BytesDownloaded = downloadInfo.Progress.BytesDownloaded,
                TotalBytes = downloadInfo.Progress.TotalBytes,
                BytesPerSecond = downloadInfo.Progress.BytesPerSecond,
                EstimatedTimeRemaining = downloadInfo.Progress.EstimatedTimeRemaining,
                Status = downloadInfo.Status,
                ErrorMessage = downloadInfo.Progress.ErrorMessage,
                StartedAt = startedAt ?? downloadInfo.Progress.StartedAt
            };
        }

        // Priority 3: Check if model is completed in repository
        if (status == null || status == ModelDownloadStatus.Completed)
        {
            var isDownloaded = await repository.ExistsAsync(modelId, cancellationToken);
            if (isDownloaded)
            {
                var model = await repository.GetModelAsync(modelId, cancellationToken);
                if (model != null)
                {
                    return CreateCompletedProgress(modelId, GetMainFileName(model), model.SizeInBytes, startedAt);
                }
            }
        }

        // Priority 4: Return status-based progress or not found
        if (status != null)
        {
            return CreateBasicProgress(modelId, status.Value, startedAt);
        }

        return CreateNotFoundProgress(modelId);
    }

    /// <summary>
    /// Creates progress for completed downloads
    /// </summary>
    public static ModelDownloadProgress CreateCompletedProgress(
        string modelId,
        string fileName,
        long totalBytes,
        DateTime? startedAt)
    {
        return new ModelDownloadProgress
        {
            ModelId = modelId,
            FileName = fileName,
            BytesDownloaded = totalBytes,
            TotalBytes = totalBytes,
            BytesPerSecond = 0,
            EstimatedTimeRemaining = TimeSpan.Zero,
            Status = ModelDownloadStatus.Completed,
            StartedAt = startedAt
        };
    }

    /// <summary>
    /// Creates progress for not found models
    /// </summary>
    public static ModelDownloadProgress CreateNotFoundProgress(string modelId)
    {
        return new ModelDownloadProgress
        {
            ModelId = modelId,
            FileName = "",
            BytesDownloaded = 0,
            TotalBytes = null,
            BytesPerSecond = 0,
            Status = ModelDownloadStatus.Failed,
            ErrorMessage = "Model not found in downloads"
        };
    }

    /// <summary>
    /// Creates basic progress without detailed information
    /// </summary>
    public static ModelDownloadProgress CreateBasicProgress(
        string modelId,
        ModelDownloadStatus status,
        DateTime? startedAt)
    {
        return new ModelDownloadProgress
        {
            ModelId = modelId,
            FileName = "",
            BytesDownloaded = 0,
            TotalBytes = null,
            BytesPerSecond = 0,
            Status = status,
            StartedAt = startedAt
        };
    }

    private static string GetMainFileName(LMModel model)
    {
        if (model.FilePaths.Count > 0)
            return model.FilePaths[0];

        if (!string.IsNullOrEmpty(model.ArtifactName))
            return $"{model.ArtifactName}.{model.Format}";

        return model.Name;
    }
}