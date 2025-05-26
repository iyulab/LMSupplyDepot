using Microsoft.AspNetCore.Mvc;

namespace LMSupplyDepots.Host.Controllers;

/// <summary>
/// Controller for local model management operations
/// </summary>
[ApiController]
[Route("api")]
public class ModelsController : ControllerBase
{
    private readonly IHostService _hostService;
    private readonly ILogger<ModelsController> _logger;

    public ModelsController(IHostService hostService, ILogger<ModelsController> logger)
    {
        _hostService = hostService;
        _logger = logger;
    }

    /// <summary>
    /// Lists available local models
    /// </summary>
    [HttpGet("models")]
    public async Task<ActionResult<LMModel[]>> ListModels(
        [FromQuery] string? type = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ModelType? modelType = null;
            if (!string.IsNullOrEmpty(type) && Enum.TryParse<ModelType>(type, true, out var parsedType))
            {
                modelType = parsedType;
            }

            var models = await _hostService.ListModelsAsync(modelType, search, cancellationToken);
            return Ok(models);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing models");
            return StatusCode(500, new ErrorResponse { Error = ex.Message });
        }
    }

    /// <summary>
    /// Shows details of a specific model
    /// </summary>
    [HttpPost("show")]
    public async Task<ActionResult<LMModel>> ShowModel([FromBody] ModelNameRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(request.Name))
        {
            return BadRequest(new ErrorResponse { Error = "Model ID or alias is required" });
        }

        try
        {
            var model = await _hostService.GetModelAsync(request.Name, cancellationToken);
            if (model == null)
            {
                return NotFound(new ErrorResponse { Error = $"Model '{request.Name}' not found" });
            }

            return Ok(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting model information for {Model}", request.Name);
            return StatusCode(500, new ErrorResponse { Error = ex.Message });
        }
    }

    /// <summary>
    /// Gets a model by alias
    /// </summary>
    [HttpGet("models/alias")]
    public async Task<ActionResult<LMModel>> GetModelByAlias(
        [FromQuery] string alias,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(alias))
        {
            return BadRequest(new ErrorResponse { Error = "Alias is required" });
        }

        try
        {
            var model = await _hostService.GetModelByAliasAsync(alias, cancellationToken);
            if (model == null)
            {
                return NotFound(new ErrorResponse { Error = $"Model with alias '{alias}' not found" });
            }

            return Ok(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting model by alias {Alias}", alias);
            return StatusCode(500, new ErrorResponse { Error = ex.Message });
        }
    }

    /// <summary>
    /// Checks if a model is downloaded
    /// </summary>
    [HttpGet("models/downloaded")]
    public async Task<ActionResult<bool>> IsModelDownloaded(
        [FromQuery] string model,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(model))
        {
            return BadRequest(new ErrorResponse { Error = "Model ID or alias is required" });
        }

        try
        {
            var isDownloaded = await _hostService.IsModelDownloadedAsync(model, cancellationToken);
            return Ok(isDownloaded);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if model {Model} is downloaded", model);
            return StatusCode(500, new ErrorResponse { Error = ex.Message });
        }
    }

    /// <summary>
    /// Deletes a model
    /// </summary>
    [HttpDelete("delete")]
    public async Task<ActionResult<StatusResponse>> DeleteModel([FromBody] ModelNameRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(request.Name))
        {
            return BadRequest(new ErrorResponse { Error = "Model ID or alias is required" });
        }

        try
        {
            if (await _hostService.IsModelLoadedAsync(request.Name, cancellationToken))
            {
                await _hostService.UnloadModelAsync(request.Name, cancellationToken);
            }

            var deleted = await _hostService.DeleteModelAsync(request.Name, cancellationToken);
            if (!deleted)
            {
                return NotFound(new ErrorResponse { Error = $"Model '{request.Name}' not found" });
            }

            return Ok(new StatusResponse { Status = "success" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting model {Model}", request.Name);
            return StatusCode(500, new ErrorResponse { Error = ex.Message });
        }
    }

    /// <summary>
    /// Creates or updates a model alias
    /// </summary>
    [HttpPut("alias")]
    public async Task<ActionResult> SetAlias([FromBody] AliasRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(request.Name))
        {
            return BadRequest(new ErrorResponse { Error = "Model ID or alias is required" });
        }

        try
        {
            var updatedModel = await _hostService.SetModelAliasAsync(request.Name, request.Alias, cancellationToken);
            return Ok(new
            {
                Status = "success",
                Message = $"Alias '{request.Alias}' set for model '{request.Name}'",
                Model = updatedModel
            });
        }
        catch (ModelNotFoundException)
        {
            return NotFound(new ErrorResponse { Error = $"Model '{request.Name}' not found" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResponse { Error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting alias {Alias} for model {ModelName}", request.Alias, request.Name);
            return StatusCode(500, new ErrorResponse { Error = "An error occurred while setting alias" });
        }
    }
}