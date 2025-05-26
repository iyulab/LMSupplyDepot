using Microsoft.AspNetCore.Mvc;

namespace LMSupplyDepots.Host.Controllers;

/// <summary>
/// Controller for model artifact download operations
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class DownloadsController : ControllerBase
{
    private readonly IHostService _hostService;
    private readonly ILogger<DownloadsController> _logger;

    public DownloadsController(IHostService hostService, ILogger<DownloadsController> logger)
    {
        _hostService = hostService;
        _logger = logger;
    }

    /// <summary>
    /// Gets the status of a specific download
    /// </summary>
    [HttpGet("status")]
    public async Task<ActionResult<ModelDownloadStatus?>> GetDownloadStatus(
        [FromQuery] string model,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(model))
        {
            return BadRequest(new ErrorResponse { Error = "Model ID or alias is required" });
        }

        try
        {
            var status = await _hostService.GetDownloadStatusAsync(model, cancellationToken);
            return Ok(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving download status for model {Model}", model);
            return StatusCode(500, new ErrorResponse
            {
                Error = $"An error occurred while retrieving download status for model {model}"
            });
        }
    }

    /// <summary>
    /// Gets the progress of a specific download
    /// </summary>
    [HttpGet("progress")]
    public async Task<ActionResult<ModelDownloadProgress?>> GetDownloadProgress(
        [FromQuery] string model,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(model))
        {
            return BadRequest(new ErrorResponse { Error = "Model ID or alias is required" });
        }

        try
        {
            var progress = await _hostService.GetDownloadProgressAsync(model, cancellationToken);
            return Ok(progress);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving download progress for model {Model}", model);
            return StatusCode(500, new ErrorResponse
            {
                Error = $"An error occurred while retrieving download progress for model {model}"
            });
        }
    }

    /// <summary>
    /// Starts downloading a specific model artifact
    /// </summary>
    [HttpPost("start")]
    public async Task<ActionResult<LMModel>> StartDownload(
        [FromBody] ModelDownloadRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(request.Model))
        {
            return BadRequest(new ErrorResponse { Error = "Model ID or alias is required" });
        }

        try
        {
            var progressTracker = new ModelDownloadProgressTracker();
            var model = await _hostService.DownloadModelAsync(request.Model, progressTracker, cancellationToken);
            return Ok(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading model {Model}", request.Model);
            return StatusCode(500, new ErrorResponse
            {
                Error = $"An error occurred while downloading model {request.Model}: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Pauses a download
    /// </summary>
    [HttpPost("pause")]
    public async Task<ActionResult> PauseDownload(
        [FromBody] ModelActionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(request.Model))
        {
            return BadRequest(new ErrorResponse { Error = "Model ID or alias is required" });
        }

        try
        {
            bool result = await _hostService.PauseDownloadAsync(request.Model, cancellationToken);
            if (!result)
            {
                return BadRequest(new ErrorResponse
                {
                    Error = $"Failed to pause download for model {request.Model}"
                });
            }
            return Ok(new { Message = $"Download paused for model {request.Model}" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pausing download for model {Model}", request.Model);
            return StatusCode(500, new ErrorResponse
            {
                Error = $"An error occurred while pausing download for model {request.Model}: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Resumes a paused download
    /// </summary>
    [HttpPost("resume")]
    public async Task<ActionResult<LMModel>> ResumeDownload(
        [FromBody] ModelActionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(request.Model))
        {
            return BadRequest(new ErrorResponse { Error = "Model ID or alias is required" });
        }

        try
        {
            var progressTracker = new ModelDownloadProgressTracker();
            var model = await _hostService.ResumeDownloadAsync(request.Model, progressTracker, cancellationToken);
            return Ok(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resuming download for model {Model}", request.Model);
            return StatusCode(500, new ErrorResponse
            {
                Error = $"An error occurred while resuming download for model {request.Model}: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Cancels a download
    /// </summary>
    [HttpPost("cancel")]
    public async Task<ActionResult> CancelDownload(
        [FromBody] ModelActionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(request.Model))
        {
            return BadRequest(new ErrorResponse { Error = "Model ID or alias is required" });
        }

        try
        {
            bool result = await _hostService.CancelDownloadAsync(request.Model, cancellationToken);
            if (!result)
            {
                return BadRequest(new ErrorResponse
                {
                    Error = $"Failed to cancel download for model {request.Model}"
                });
            }
            return Ok(new { Message = $"Download cancelled for model {request.Model}" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling download for model {Model}", request.Model);
            return StatusCode(500, new ErrorResponse
            {
                Error = $"An error occurred while cancelling download for model {request.Model}: {ex.Message}"
            });
        }
    }
}

/// <summary>
/// Request model for starting a download
/// </summary>
public class ModelDownloadRequest
{
    /// <summary>
    /// Model ID or alias to download
    /// </summary>
    public string Model { get; set; } = string.Empty;
}

/// <summary>
/// Request model for download actions (pause, resume, cancel)
/// </summary>
public class ModelActionRequest
{
    /// <summary>
    /// Model ID or alias
    /// </summary>
    public string Model { get; set; } = string.Empty;
}

/// <summary>
/// Simple progress tracker for model downloads
/// </summary>
public class ModelDownloadProgressTracker : IProgress<ModelDownloadProgress>
{
    public ModelDownloadProgress? LastProgress { get; private set; }

    public void Report(ModelDownloadProgress value)
    {
        LastProgress = value;
    }
}