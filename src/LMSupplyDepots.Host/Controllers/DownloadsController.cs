using Microsoft.AspNetCore.Mvc;

namespace LMSupplyDepots.Host.Controllers;

/// <summary>
/// Controller for model artifact download operations
/// </summary>
[ApiController]
[Route("api/downloads")]
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
    /// Gets all current downloads (active, paused, etc.)
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<DownloadInfo>>> GetAllDownloads(CancellationToken cancellationToken = default)
    {
        try
        {
            var downloads = await _hostService.GetAllDownloadsAsync(cancellationToken);
            return Ok(downloads);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all downloads");
            return StatusCode(500, new ErrorResponse
            {
                Error = "An error occurred while retrieving downloads"
            });
        }
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
    /// Starts downloading a specific model artifact and returns immediately
    /// </summary>
    [HttpPost("start")]
    public async Task<ActionResult> StartDownload(
        [FromBody] ModelDownloadRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(request.Model))
        {
            return BadRequest(new ErrorResponse { Error = "Model ID or alias is required" });
        }

        try
        {
            // Check if already downloading
            var currentStatus = await _hostService.GetDownloadStatusAsync(request.Model, cancellationToken);
            if (currentStatus == ModelDownloadStatus.Downloading)
            {
                return Conflict(new ErrorResponse
                {
                    Error = $"Model {request.Model} is already being downloaded"
                });
            }

            if (currentStatus == ModelDownloadStatus.Completed)
            {
                return Ok(new
                {
                    Message = $"Model {request.Model} is already downloaded",
                    Model = request.Model,
                    Status = "Completed"
                });
            }

            // Start download in background
            _ = Task.Run(async () =>
            {
                try
                {
                    var progressTracker = new ModelDownloadProgressTracker();
                    await _hostService.DownloadModelAsync(request.Model, progressTracker, CancellationToken.None);
                    _logger.LogInformation("Background download completed for model {Model}", request.Model);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Background download failed for model {Model}", request.Model);
                }
            });

            return Ok(new
            {
                Message = $"Download started for model {request.Model}",
                Model = request.Model,
                Status = "Started"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting download for model {Model}", request.Model);
            return StatusCode(500, new ErrorResponse
            {
                Error = $"An error occurred while starting download for model {request.Model}: {ex.Message}"
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
            // Check current status first
            var currentStatus = await _hostService.GetDownloadStatusAsync(request.Model, cancellationToken);

            if (currentStatus == null)
            {
                return NotFound(new ErrorResponse
                {
                    Error = $"No download found for model {request.Model}"
                });
            }

            if (currentStatus == ModelDownloadStatus.Completed)
            {
                return Ok(new
                {
                    Message = $"Model {request.Model} is already completed",
                    Model = request.Model,
                    Status = "Completed"
                });
            }

            if (currentStatus == ModelDownloadStatus.Paused)
            {
                return Ok(new
                {
                    Message = $"Model {request.Model} is already paused",
                    Model = request.Model,
                    Status = "Paused"
                });
            }

            if (currentStatus != ModelDownloadStatus.Downloading)
            {
                return BadRequest(new ErrorResponse
                {
                    Error = $"Cannot pause model {request.Model} - current status: {currentStatus}"
                });
            }

            bool result = await _hostService.PauseDownloadAsync(request.Model, cancellationToken);
            if (!result)
            {
                return BadRequest(new ErrorResponse
                {
                    Error = $"Failed to pause download for model {request.Model}"
                });
            }

            return Ok(new
            {
                Message = $"Download paused for model {request.Model}",
                Model = request.Model,
                Status = "Paused"
            });
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
    /// Resumes a paused download and returns immediately
    /// </summary>
    [HttpPost("resume")]
    public async Task<ActionResult> ResumeDownload(
        [FromBody] ModelActionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(request.Model))
        {
            return BadRequest(new ErrorResponse { Error = "Model ID or alias is required" });
        }

        try
        {
            // Check if download is paused
            var currentStatus = await _hostService.GetDownloadStatusAsync(request.Model, cancellationToken);

            if (currentStatus == null)
            {
                return NotFound(new ErrorResponse
                {
                    Error = $"No download found for model {request.Model}"
                });
            }

            if (currentStatus == ModelDownloadStatus.Completed)
            {
                return Ok(new
                {
                    Message = $"Model {request.Model} is already completed",
                    Model = request.Model,
                    Status = "Completed"
                });
            }

            if (currentStatus == ModelDownloadStatus.Downloading)
            {
                return Ok(new
                {
                    Message = $"Model {request.Model} is already downloading",
                    Model = request.Model,
                    Status = "Downloading"
                });
            }

            if (currentStatus != ModelDownloadStatus.Paused)
            {
                return BadRequest(new ErrorResponse
                {
                    Error = $"Cannot resume model {request.Model} - current status: {currentStatus}"
                });
            }

            // Resume download in background
            _ = Task.Run(async () =>
            {
                try
                {
                    var progressTracker = new ModelDownloadProgressTracker();
                    await _hostService.ResumeDownloadAsync(request.Model, progressTracker, CancellationToken.None);
                    _logger.LogInformation("Background resume completed for model {Model}", request.Model);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Background resume failed for model {Model}", request.Model);
                }
            });

            return Ok(new
            {
                Message = $"Download resumed for model {request.Model}",
                Model = request.Model,
                Status = "Resumed"
            });
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
            // Check current status first
            var currentStatus = await _hostService.GetDownloadStatusAsync(request.Model, cancellationToken);

            if (currentStatus == null)
            {
                return NotFound(new ErrorResponse
                {
                    Error = $"No download found for model {request.Model}"
                });
            }

            if (currentStatus == ModelDownloadStatus.Completed)
            {
                return Ok(new
                {
                    Message = $"Model {request.Model} is already completed",
                    Model = request.Model,
                    Status = "Completed"
                });
            }

            bool result = await _hostService.CancelDownloadAsync(request.Model, cancellationToken);
            if (!result)
            {
                return BadRequest(new ErrorResponse
                {
                    Error = $"Failed to cancel download for model {request.Model}"
                });
            }

            return Ok(new
            {
                Message = $"Download cancelled for model {request.Model}",
                Model = request.Model,
                Status = "Cancelled"
            });
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