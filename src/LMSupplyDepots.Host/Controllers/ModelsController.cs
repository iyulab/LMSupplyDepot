using Microsoft.AspNetCore.Mvc;
using LMSupplyDepots.Models;
using LMSupplyDepots.ModelHub.Models;
using Microsoft.Extensions.Logging;
using LMSupplyDepots.Contracts;
using LMSupplyDepots.ModelHub.Exceptions;

namespace LMSupplyDepots.Host.Controllers;

/// <summary>
/// Controller for model management operations (Ollama-style API)
/// </summary>
[ApiController]
[Route("api")]
public class ModelController : ControllerBase
{
    private readonly IHostService _hostService;
    private readonly ILogger<ModelController> _logger;

    /// <summary>
    /// Initializes a new instance of the ModelController
    /// </summary>
    public ModelController(IHostService hostService, ILogger<ModelController> logger)
    {
        _hostService = hostService;
        _logger = logger;
    }

    /// <summary>
    /// Lists available models
    /// </summary>
    [HttpGet("models")]
    public async Task<ActionResult<LMModel[]>> ListModels()
    {
        try
        {
            var models = await _hostService.ListModelsAsync();
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
    public async Task<ActionResult<LMModel>> ShowModel(
        [FromBody] ModelNameRequest request)
    {
        if (string.IsNullOrEmpty(request.Name))
        {
            return BadRequest(new ErrorResponse { Error = "Model name is required" });
        }

        try
        {
            var model = await _hostService.GetModelAsync(request.Name);
            if (model == null)
            {
                return NotFound(new ErrorResponse { Error = $"Model '{request.Name}' not found" });
            }

            return Ok(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting model information for {ModelId}", request.Name);
            return StatusCode(500, new ErrorResponse { Error = ex.Message });
        }
    }

    /// <summary>
    /// Deletes a model
    /// </summary>
    [HttpDelete("delete")]
    public async Task<ActionResult<StatusResponse>> DeleteModel(
        [FromBody] ModelNameRequest request)
    {
        if (string.IsNullOrEmpty(request.Name))
        {
            return BadRequest(new ErrorResponse { Error = "Model name is required" });
        }

        try
        {
            // Unload the model first if it's loaded
            if (await _hostService.IsModelLoadedAsync(request.Name))
            {
                await _hostService.UnloadModelAsync(request.Name);
            }

            // Delete the model
            var deleted = await _hostService.DeleteModelAsync(request.Name);
            if (!deleted)
            {
                return NotFound(new ErrorResponse { Error = $"Model '{request.Name}' not found" });
            }

            return Ok(new StatusResponse
            {
                Status = "success"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting model {ModelId}", request.Name);
            return StatusCode(500, new ErrorResponse { Error = ex.Message });
        }
    }

    /// <summary>
    /// Searches for models (external repositories)
    /// </summary>
    [HttpGet("search")]
    public async Task<ActionResult<SearchResponse>> SearchModels(
        [FromQuery] string? q = null, [FromQuery] string? type = null, [FromQuery] int limit = 10)
    {
        try
        {
            ModelType? modelType = null;
            if (!string.IsNullOrEmpty(type) && Enum.TryParse<ModelType>(type, true, out var parsedType))
            {
                modelType = parsedType;
            }

            var results = await _hostService.SearchModelsAsync(modelType, q, limit);

            // Convert to Ollama-like response
            var response = new SearchResponse
            {
                Models = results.Select(m => new SearchModelInfo
                {
                    Name = m.Model.Id,
                    Description = m.Model.Description,
                    Size = m.Model.SizeInBytes,
                    Format = m.Model.Format,
                    IsDownloaded = m.IsDownloaded,
                    Source = m.SourceName
                }).ToList()
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching for models");
            return StatusCode(500, new ErrorResponse { Error = ex.Message });
        }
    }

    /// <summary>
    /// Creates or updates a model alias
    /// </summary>
    [HttpPut("alias")]
    public async Task<ActionResult> SetAlias([FromBody] AliasRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.Name))
        {
            return BadRequest(new ErrorResponse { Error = "Model name is required" });
        }

        var alias = request.Alias;

        try
        {
            // Use the host service to set the alias
            var updatedModel = await _hostService.SetModelAliasAsync(request.Name, alias, cancellationToken);
            return Ok(new { Status = "success", Message = $"Alias '{alias}' set for model '{request.Name}'", Model = updatedModel });
        }
        catch (ModelNotFoundException)
        {
            return NotFound($"Model '{request.Name}' not found");
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting alias {Alias} for model {ModelName}", alias, request.Name);
            return StatusCode(500, $"An error occurred while setting alias '{alias}' for model '{request.Name}'");
        }
    }
}