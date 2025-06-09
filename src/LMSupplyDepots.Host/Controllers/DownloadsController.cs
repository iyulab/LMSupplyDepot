using Microsoft.AspNetCore.Mvc;

namespace LMSupplyDepots.Host.Controllers;

/// <summary>
/// Controller for model download operations
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
    /// Gets all current downloads
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
            return StatusCode(500, new ErrorResponse { Error = "An error occurred while retrieving downloads" });
        }
    }

    /// <summary>
    /// Gets detailed download status including size and progress
    /// </summary>
    [HttpGet("status")]
    public async Task<ActionResult<ModelDownloadProgress?>> GetDownloadStatus(
        [FromQuery] string model,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(model))
            return BadRequest(new ErrorResponse { Error = "Model ID or alias is required" });

        try
        {
            var progress = await _hostService.GetDownloadProgressAsync(model, cancellationToken);
            return Ok(progress);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving download status for model {Model}", model);
            return StatusCode(500, new ErrorResponse { Error = $"An error occurred while retrieving download status for model {model}" });
        }
    }

    /// <summary>
    /// Gets simple download status for backward compatibility
    /// </summary>
    [HttpGet("status/simple")]
    public async Task<ActionResult<ModelDownloadStatus?>> GetDownloadStatusSimple(
        [FromQuery] string model,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(model))
            return BadRequest(new ErrorResponse { Error = "Model ID or alias is required" });

        try
        {
            var status = await _hostService.GetDownloadStatusAsync(model, cancellationToken);
            return Ok(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving simple download status for model {Model}", model);
            return StatusCode(500, new ErrorResponse { Error = $"An error occurred while retrieving download status for model {model}" });
        }
    }

    /// <summary>
    /// Starts downloading a model
    /// </summary>
    [HttpPost("start")]
    public async Task<ActionResult> StartDownload([FromBody] ModelDownloadRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(request.Model))
            return BadRequest(new ErrorResponse { Error = "Model ID or alias is required" });

        try
        {
            var currentProgress = await _hostService.GetDownloadProgressAsync(request.Model, cancellationToken);

            if (currentProgress?.Status == ModelDownloadStatus.Downloading)
                return Conflict(new ErrorResponse { Error = $"Model {request.Model} is already being downloaded" });

            if (currentProgress?.Status == ModelDownloadStatus.Completed)
                return Ok(CreateStatusResponse($"Model {request.Model} is already downloaded", request.Model, "Completed"));

            // Start download in background
            _ = StartDownloadInBackground(request.Model);

            return Ok(CreateStatusResponse($"Download started for model {request.Model}", request.Model, "Started"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting download for model {Model}", request.Model);
            return StatusCode(500, new ErrorResponse { Error = $"An error occurred while starting download for model {request.Model}: {ex.Message}" });
        }
    }

    /// <summary>
    /// Pauses a download
    /// </summary>
    [HttpPost("pause")]
    public async Task<ActionResult> PauseDownload([FromBody] ModelActionRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(request.Model))
            return BadRequest(new ErrorResponse { Error = "Model ID or alias is required" });

        try
        {
            var currentProgress = await _hostService.GetDownloadProgressAsync(request.Model, cancellationToken);
            var validationResult = ValidateDownloadAction(currentProgress, request.Model, "pause", ModelDownloadStatus.Downloading);

            if (validationResult != null)
                return validationResult;

            bool result = await _hostService.PauseDownloadAsync(request.Model, cancellationToken);
            return result
                ? Ok(CreateStatusResponse($"Download paused for model {request.Model}", request.Model, "Paused"))
                : BadRequest(new ErrorResponse { Error = $"Failed to pause download for model {request.Model}" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pausing download for model {Model}", request.Model);
            return StatusCode(500, new ErrorResponse { Error = $"An error occurred while pausing download for model {request.Model}: {ex.Message}" });
        }
    }

    /// <summary>
    /// Resumes a paused download
    /// </summary>
    [HttpPost("resume")]
    public async Task<ActionResult> ResumeDownload([FromBody] ModelActionRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(request.Model))
            return BadRequest(new ErrorResponse { Error = "Model ID or alias is required" });

        try
        {
            var currentProgress = await _hostService.GetDownloadProgressAsync(request.Model, cancellationToken);
            var validationResult = ValidateDownloadAction(currentProgress, request.Model, "resume", ModelDownloadStatus.Paused);

            if (validationResult != null)
                return validationResult;

            // Resume download in background
            _ = ResumeDownloadInBackground(request.Model);

            return Ok(CreateStatusResponse($"Download resumed for model {request.Model}", request.Model, "Resumed"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resuming download for model {Model}", request.Model);
            return StatusCode(500, new ErrorResponse { Error = $"An error occurred while resuming download for model {request.Model}: {ex.Message}" });
        }
    }

    /// <summary>
    /// Cancels a download
    /// </summary>
    [HttpPost("cancel")]
    public async Task<ActionResult> CancelDownload([FromBody] ModelActionRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(request.Model))
            return BadRequest(new ErrorResponse { Error = "Model ID or alias is required" });

        try
        {
            var currentProgress = await _hostService.GetDownloadProgressAsync(request.Model, cancellationToken);

            if (currentProgress == null)
                return NotFound(new ErrorResponse { Error = $"No download found for model {request.Model}" });

            if (currentProgress.Status == ModelDownloadStatus.Completed)
                return Ok(CreateStatusResponse($"Model {request.Model} is already completed", request.Model, "Completed"));

            bool result = await _hostService.CancelDownloadAsync(request.Model, cancellationToken);
            return result
                ? Ok(CreateStatusResponse($"Download cancelled for model {request.Model}", request.Model, "Cancelled"))
                : BadRequest(new ErrorResponse { Error = $"Failed to cancel download for model {request.Model}" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling download for model {Model}", request.Model);
            return StatusCode(500, new ErrorResponse { Error = $"An error occurred while cancelling download for model {request.Model}: {ex.Message}" });
        }
    }

    #region Private Helpers

    private ActionResult? ValidateDownloadAction(ModelDownloadProgress? currentProgress, string model, string action, ModelDownloadStatus requiredStatus)
    {
        if (currentProgress == null)
            return NotFound(new ErrorResponse { Error = $"No download found for model {model}" });

        if (currentProgress.Status == ModelDownloadStatus.Completed)
            return Ok(CreateStatusResponse($"Model {model} is already completed", model, "Completed"));

        if (currentProgress.Status == requiredStatus)
            return Ok(CreateStatusResponse($"Model {model} is already {requiredStatus.ToString().ToLower()}", model, requiredStatus.ToString()));

        if (currentProgress.Status != requiredStatus && action == "pause" && requiredStatus == ModelDownloadStatus.Downloading)
            return BadRequest(new ErrorResponse { Error = $"Cannot pause model {model} - current status: {currentProgress.Status}" });

        if (currentProgress.Status != requiredStatus && action == "resume" && requiredStatus == ModelDownloadStatus.Paused)
            return BadRequest(new ErrorResponse { Error = $"Cannot resume model {model} - current status: {currentProgress.Status}" });

        return null;
    }

    private object CreateStatusResponse(string message, string model, string status) =>
        new { Message = message, Model = model, Status = status };

    private Task StartDownloadInBackground(string model) =>
        Task.Run(async () =>
        {
            try
            {
                await _hostService.DownloadModelAsync(model, null, CancellationToken.None);
                _logger.LogInformation("Background download completed for model {Model}", model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background download failed for model {Model}", model);
            }
        });

    private Task ResumeDownloadInBackground(string model) =>
        Task.Run(async () =>
        {
            try
            {
                await _hostService.ResumeDownloadAsync(model, null, CancellationToken.None);
                _logger.LogInformation("Background resume completed for model {Model}", model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background resume failed for model {Model}", model);
            }
        });

    #endregion
}

/// <summary>
/// Request model for starting a download
/// </summary>
public class ModelDownloadRequest
{
    public string Model { get; set; } = string.Empty;
}

/// <summary>
/// Request model for download actions
/// </summary>
public class ModelActionRequest
{
    public string Model { get; set; } = string.Empty;
}