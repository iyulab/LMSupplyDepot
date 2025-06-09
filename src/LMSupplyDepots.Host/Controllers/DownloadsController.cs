using Microsoft.AspNetCore.Mvc;

namespace LMSupplyDepots.Host.Controllers;

/// <summary>
/// Controller for model download operations - simplified and delegates all logic to SDK
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
            return StatusCode(500, new ErrorResponse { Error = "Failed to retrieve downloads" });
        }
    }

    /// <summary>
    /// Gets download progress for a model
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
            return StatusCode(500, new ErrorResponse { Error = "Failed to retrieve download status" });
        }
    }

    /// <summary>
    /// Starts downloading a model
    /// </summary>
    [HttpPost("start")]
    public async Task<ActionResult> StartDownload([FromBody] DownloadRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(request.Model))
            return BadRequest(new ErrorResponse { Error = "Model ID or alias is required" });

        try
        {
            var result = await _hostService.StartDownloadAsync(request.Model, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ErrorResponse { Error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting download for model {Model}", request.Model);
            return StatusCode(500, new ErrorResponse { Error = "Failed to start download" });
        }
    }

    /// <summary>
    /// Pauses a download
    /// </summary>
    [HttpPost("pause")]
    public async Task<ActionResult> PauseDownload([FromBody] DownloadRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(request.Model))
            return BadRequest(new ErrorResponse { Error = "Model ID or alias is required" });

        try
        {
            var result = await _hostService.PauseDownloadAsync(request.Model, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pausing download for model {Model}", request.Model);
            return StatusCode(500, new ErrorResponse { Error = "Failed to pause download" });
        }
    }

    /// <summary>
    /// Resumes a paused download
    /// </summary>
    [HttpPost("resume")]
    public async Task<ActionResult> ResumeDownload([FromBody] DownloadRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(request.Model))
            return BadRequest(new ErrorResponse { Error = "Model ID or alias is required" });

        try
        {
            var result = await _hostService.ResumeDownloadAsync(request.Model, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resuming download for model {Model}", request.Model);
            return StatusCode(500, new ErrorResponse { Error = "Failed to resume download" });
        }
    }

    /// <summary>
    /// Cancels a download
    /// </summary>
    [HttpPost("cancel")]
    public async Task<ActionResult> CancelDownload([FromBody] DownloadRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(request.Model))
            return BadRequest(new ErrorResponse { Error = "Model ID or alias is required" });

        try
        {
            var result = await _hostService.CancelDownloadAsync(request.Model, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling download for model {Model}", request.Model);
            return StatusCode(500, new ErrorResponse { Error = "Failed to cancel download" });
        }
    }
}

/// <summary>
/// Request model for download operations
/// </summary>
public class DownloadRequest
{
    public string Model { get; set; } = string.Empty;
}