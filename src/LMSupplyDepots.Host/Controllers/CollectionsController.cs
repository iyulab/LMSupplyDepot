using LMSupplyDepots.External.HuggingFace.Models;
using Microsoft.AspNetCore.Mvc;

namespace LMSupplyDepots.Host.Controllers;

/// <summary>
/// Controller for discovering model collections from external hubs
/// </summary>
[ApiController]
[Route("api/collections")]
public class CollectionsController : ControllerBase
{
    private readonly IHostService _hostService;
    private readonly ILogger<CollectionsController> _logger;

    public CollectionsController(IHostService hostService, ILogger<CollectionsController> logger)
    {
        _hostService = hostService;
        _logger = logger;
    }

    /// <summary>
    /// Discovers model collections from external hubs
    /// </summary>
    [HttpGet("discover")]
    public async Task<ActionResult<IReadOnlyList<LMCollection>>> DiscoverCollections(
        [FromQuery] string? q = null,
        [FromQuery] string? type = null,
        [FromQuery] int limit = 10,
        [FromQuery] ModelSortField sort = ModelSortField.Downloads,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ModelType? modelType = null;
            if (!string.IsNullOrEmpty(type) && Enum.TryParse<ModelType>(type, true, out var parsedType))
            {
                modelType = parsedType;
            }

            var collections = await _hostService.DiscoverCollectionsAsync(modelType, q, limit, sort, cancellationToken);
            return Ok(collections);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering collections");
            return StatusCode(500, new ErrorResponse { Error = ex.Message });
        }
    }

    /// <summary>
    /// Gets detailed information about a specific collection
    /// </summary>
    [HttpGet("info")]
    public async Task<ActionResult<LMCollection>> GetCollectionInfo(
        [FromQuery] string collectionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(collectionId))
        {
            return BadRequest(new ErrorResponse { Error = "Collection ID is required" });
        }

        try
        {
            var collection = await _hostService.GetCollectionInfoAsync(collectionId, cancellationToken);
            return Ok(collection);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting collection info for {CollectionId}", collectionId);
            return StatusCode(500, new ErrorResponse { Error = ex.Message });
        }
    }

    /// <summary>
    /// Gets all available models (artifacts) from a collection
    /// </summary>
    [HttpGet("models")]
    public async Task<ActionResult<IReadOnlyList<LMModel>>> GetCollectionModels(
        [FromQuery] string collectionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(collectionId))
        {
            return BadRequest(new ErrorResponse { Error = "Collection ID is required" });
        }

        try
        {
            var models = await _hostService.GetCollectionModelsAsync(collectionId, cancellationToken);
            return Ok(models);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting models for collection {CollectionId}", collectionId);
            return StatusCode(500, new ErrorResponse { Error = ex.Message });
        }
    }
}