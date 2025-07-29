using Microsoft.AspNetCore.Mvc;
using LMSupplyDepots.Models;
using Microsoft.Extensions.Logging;
using LMSupplyDepots.Contracts;

namespace LMSupplyDepots.Host.Controllers;

/// <summary>
/// Controller for explicit model loading and unloading operations
/// </summary>
[ApiController]
[Route("api/models")]
public class ModelLoadController : ControllerBase
{
    private readonly IHostService _hostService;
    private readonly ILogger<ModelLoadController> _logger;

    /// <summary>
    /// Initializes a new instance of the ModelLoadController
    /// </summary>
    public ModelLoadController(IHostService hostService, ILogger<ModelLoadController> logger)
    {
        _hostService = hostService;
        _logger = logger;
    }

    /// <summary>
    /// Explicitly loads a model into memory
    /// </summary>
    [HttpPost("load")]
    public async Task<ActionResult<LMModel>> LoadModel([FromBody] ModelLoadRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.Model))
        {
            return BadRequest(new ErrorResponse { Error = "Model ID is required" });
        }

        try
        {
            // Check if the model exists
            var model = await _hostService.GetModelAsync(request.Model, cancellationToken);
            if (model == null)
            {
                return NotFound(new ErrorResponse { Error = $"Model '{request.Model}' not found" });
            }

            // Check if the model is already loaded
            if (await _hostService.IsModelLoadedAsync(request.Model, cancellationToken))
            {
                return Ok(new
                {
                    Message = $"Model '{request.Model}' is already loaded",
                    Model = model
                });
            }

            // Load the model
            var loadedModel = await _hostService.LoadModelAsync(
                request.Model,
                request.Parameters,
                cancellationToken);

            _logger.LogInformation("Model '{ModelId}' loaded successfully", request.Model);

            return Ok(new
            {
                Message = $"Model '{request.Model}' loaded successfully",
                Model = loadedModel
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading model '{ModelId}'", request.Model);
            return StatusCode(500, new ErrorResponse { Error = $"Error loading model: {ex.Message}" });
        }
    }

    /// <summary>
    /// Unloads a model from memory
    /// </summary>
    [HttpPost("unload")]
    public async Task<ActionResult> UnloadModel([FromBody] ModelUnloadRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.Model))
        {
            return BadRequest(new ErrorResponse { Error = "Model ID is required" });
        }

        try
        {
            // Check if the model is loaded
            if (!await _hostService.IsModelLoadedAsync(request.Model, cancellationToken))
            {
                return Ok(new { Message = $"Model '{request.Model}' is not currently loaded" });
            }

            // Unload the model
            await _hostService.UnloadModelAsync(request.Model, cancellationToken);

            _logger.LogInformation("Model '{ModelId}' unloaded successfully", request.Model);
            return Ok(new { Message = $"Model '{request.Model}' unloaded successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unloading model '{ModelId}'", request.Model);
            return StatusCode(500, new ErrorResponse { Error = $"Error unloading model: {ex.Message}" });
        }
    }

    /// <summary>
    /// Gets a list of all currently loaded models
    /// </summary>
    [HttpGet("loaded")]
    public async Task<ActionResult<IEnumerable<LMModel>>> GetLoadedModels(CancellationToken cancellationToken)
    {
        try
        {
            var loadedModels = await _hostService.GetLoadedModelsAsync(cancellationToken);
            return Ok(loadedModels);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving loaded models");
            return StatusCode(500, new ErrorResponse { Error = $"Error retrieving loaded models: {ex.Message}" });
        }
    }

    /// <summary>
    /// Gets the runtime state of a model
    /// </summary>
    [HttpGet("{modelKey}/status")]
    public async Task<ActionResult<ModelRuntimeState>> GetModelStatus(string modelKey, CancellationToken cancellationToken)
    {
        try
        {
            var runtimeState = await _hostService.GetModelRuntimeStateAsync(modelKey, cancellationToken);
            return Ok(runtimeState);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving status for model '{ModelKey}'", modelKey);
            return StatusCode(500, new ErrorResponse { Error = $"Error retrieving model status: {ex.Message}" });
        }
    }

    /// <summary>
    /// Gets runtime states for all models
    /// </summary>
    [HttpGet("status")]
    public ActionResult<IReadOnlyDictionary<string, ModelRuntimeState>> GetAllModelStatuses()
    {
        try
        {
            var allStates = _hostService.GetAllModelRuntimeStates();
            return Ok(allStates);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all model statuses");
            return StatusCode(500, new ErrorResponse { Error = $"Error retrieving model statuses: {ex.Message}" });
        }
    }
}