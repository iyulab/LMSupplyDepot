using LMSupplyDepots.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Humanizer;
using LMSupplyDepots.Utils;

namespace LMSupplyDepots.Host.Controllers;

/// <summary>
/// Controller for v1 API inference operations (OpenAI-compatible)
/// </summary>
[ApiController]
[Route("/v1")]
public class V1Controller : ControllerBase
{
    private readonly IHostService _hostService;
    private readonly ILogger<V1Controller> _logger;

    /// <summary>
    /// Initializes a new instance of the V1Controller
    /// </summary>
    public V1Controller(IHostService hostService, ILogger<V1Controller> logger)
    {
        _hostService = hostService;
        _logger = logger;
    }

    /// <summary>
    /// Lists all loaded models with alias taking precedence over ID
    /// </summary>
    [HttpGet("models")]
    public async Task<ActionResult<ModelsListResponse>> ListModels(CancellationToken cancellationToken)
    {
        try
        {
            // Get all loaded models from the host service
            var loadedModels = await _hostService.GetLoadedModelsAsync(cancellationToken);

            // Convert to the response format, using Key (alias or id) for the name
            var response = new ModelsListResponse
            {
                Models = loadedModels.Select(m => new ModelListItem
                {
                    Type = m.Type.ToString().ToDashCase(),
                    Name = m.Key // This uses Alias if available, otherwise Id
                }).ToList()
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing models");
            return StatusCode(500, "An error occurred while listing models");
        }
    }

    /// <summary>
    /// Generates embeddings for the provided texts
    /// </summary>
    [HttpPost("embeddings")]
    public async Task<ActionResult<EmbeddingResponse>> GenerateEmbeddings(
        [FromBody] EmbeddingRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.Model))
        {
            return BadRequest("Model is required");
        }

        if (request == null || request.Texts == null || request.Texts.Count == 0)
        {
            return BadRequest("Request must include at least one text to embed");
        }

        try
        {
            // Check if the model exists
            var model = await _hostService.GetModelAsync(request.Model, cancellationToken);
            if (model == null)
            {
                return NotFound($"Model '{request.Model}' not found");
            }

            // Check if the model supports embeddings
            if (!model.Capabilities.SupportsEmbeddings)
            {
                return BadRequest($"Model '{request.Model}' does not support embeddings");
            }

            // Generate embeddings
            var response = await _hostService.GenerateEmbeddingsAsync(request.Model, request, cancellationToken);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating embeddings with model {Model}", request.Model);
            return StatusCode(500, $"Error generating embeddings: {ex.Message}");
        }
    }

    /// <summary>
    /// Generates text based on the provided prompt
    /// </summary>
    [HttpPost("chat/completions")]
    public async Task<ActionResult<GenerationResponse>> GenerateText(
        [FromBody] GenerationRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.Model))
        {
            return BadRequest("Model is required");
        }

        if (request == null || string.IsNullOrEmpty(request.Prompt))
        {
            return BadRequest("Request must include a prompt");
        }

        try
        {
            // Check if the model exists
            var model = await _hostService.GetModelAsync(request.Model, cancellationToken);
            if (model == null)
            {
                return NotFound($"Model '{request.Model}' not found");
            }

            // Check if the model supports text generation
            if (model.Type != Models.ModelType.TextGeneration || !model.Capabilities.SupportsTextGeneration)
            {
                return BadRequest($"Model '{request.Model}' does not support text generation");
            }

            // Handle streaming requests
            if (request.Stream)
            {
                return await GenerateTextStream(request, cancellationToken);
            }

            // Generate text
            var response = await _hostService.GenerateTextAsync(request.Model, request, cancellationToken);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating text with model {Model}", request.Model);
            return StatusCode(500, $"Error generating text: {ex.Message}");
        }
    }

    /// <summary>
    /// Generates text with streaming response
    /// </summary>
    [HttpPost("chat/completions/stream")]
    public async Task<ActionResult> GenerateTextStreamEndpoint(
        [FromBody] GenerationRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.Model))
        {
            return BadRequest("Model is required");
        }

        if (request == null || string.IsNullOrEmpty(request.Prompt))
        {
            return BadRequest("Request must include a prompt");
        }

        // Force streaming mode
        request.Stream = true;

        return await GenerateTextStream(request, cancellationToken);
    }

    /// <summary>
    /// Internal method to handle streaming text generation
    /// </summary>
    private async Task<ActionResult> GenerateTextStream(GenerationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            // Check if the model exists
            var model = await _hostService.GetModelAsync(request.Model, cancellationToken);
            if (model == null)
            {
                Response.StatusCode = 404;
                await Response.WriteAsync($"Model '{request.Model}' not found");
                return new EmptyResult();
            }

            // Check if the model supports text generation
            if (model.Type != Models.ModelType.TextGeneration || !model.Capabilities.SupportsTextGeneration)
            {
                Response.StatusCode = 400;
                await Response.WriteAsync($"Model '{request.Model}' does not support text generation");
                return new EmptyResult();
            }

            // Set the response type for streaming
            Response.ContentType = "text/event-stream";
            Response.Headers.Add("Cache-Control", "no-cache");
            Response.Headers.Add("Connection", "keep-alive");

            // Stream the generated text
            await foreach (var token in _hostService.GenerateTextStreamAsync(request.Model, request, cancellationToken))
            {
                var data = new { text = token };
                var json = JsonHelper.Serialize(data);
                await Response.WriteAsync($"data: {json}\n\n");
                await Response.Body.FlushAsync(cancellationToken);
            }

            // Send completion signal
            await Response.WriteAsync("data: [DONE]\n\n");
            await Response.Body.FlushAsync(cancellationToken);

            return new EmptyResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error streaming text generation with model {Model}", request.Model);
            Response.StatusCode = 500;
            await Response.WriteAsync($"Error generating text: {ex.Message}");
            return new EmptyResult();
        }
    }
}