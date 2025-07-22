using LMSupplyDepots.Contracts;
using LMSupplyDepots.Host.Models.OpenAI;
using LMSupplyDepots.Host.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Text.Json;
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
    private readonly IOpenAIConverterService _converterService;
    private readonly ILogger<V1Controller> _logger;

    /// <summary>
    /// Initializes a new instance of the V1Controller
    /// </summary>
    public V1Controller(
        IHostService hostService,
        IOpenAIConverterService converterService,
        ILogger<V1Controller> logger)
    {
        _hostService = hostService;
        _converterService = converterService;
        _logger = logger;
    }

    /// <summary>
    /// Lists all available models (OpenAI-compatible)
    /// </summary>
    [HttpGet("models")]
    public async Task<ActionResult<OpenAIModelsResponse>> ListModels(CancellationToken cancellationToken)
    {
        try
        {
            var loadedModels = await _hostService.GetLoadedModelsAsync(cancellationToken);
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            var response = new OpenAIModelsResponse
            {
                Data = loadedModels.Select(m => _converterService.ConvertToOpenAIModel(m, timestamp)).ToList()
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing models");
            return StatusCode(500, CreateErrorResponse("internal_error", "An error occurred while listing models"));
        }
    }

    /// <summary>
    /// Creates a chat completion (OpenAI-compatible)
    /// </summary>
    [HttpPost("chat/completions")]
    public async Task<ActionResult> CreateChatCompletion(
        [FromBody] OpenAIChatCompletionRequest request,
        CancellationToken cancellationToken)
    {
        // Validate request object
        if (request == null)
        {
            return BadRequest(CreateErrorResponse("invalid_request_error", "Request body is required"));
        }

        // Validate request
        if (string.IsNullOrEmpty(request.Model))
        {
            return BadRequest(CreateErrorResponse("invalid_request_error", "Model is required", "model"));
        }

        if (request.Messages == null || request.Messages.Count == 0)
        {
            return BadRequest(CreateErrorResponse("invalid_request_error", "Messages are required", "messages"));
        }

        // Validate messages content
        foreach (var message in request.Messages)
        {
            if (string.IsNullOrEmpty(message.Role))
            {
                return BadRequest(CreateErrorResponse("invalid_request_error", "Message role is required", "messages"));
            }

            // Content is required for most roles except assistant with tool_calls
            if (message.Content == null &&
                (message.Role.ToLowerInvariant() != "assistant" || message.ToolCalls == null || message.ToolCalls.Count == 0))
            {
                return BadRequest(CreateErrorResponse("invalid_request_error", "Message content is required", "messages"));
            }

            // Validate role values (including new roles)
            var validRoles = new[] { "system", "user", "assistant", "tool", "developer" };
            if (!validRoles.Contains(message.Role.ToLowerInvariant()))
            {
                return BadRequest(CreateErrorResponse("invalid_request_error",
                    $"Invalid message role '{message.Role}'. Must be one of: {string.Join(", ", validRoles)}",
                    "messages"));
            }

            // Validate tool message requirements
            if (message.Role.ToLowerInvariant() == "tool" && string.IsNullOrEmpty(message.ToolCallId))
            {
                return BadRequest(CreateErrorResponse("invalid_request_error", "Tool messages must include tool_call_id", "messages"));
            }
        }

        // Validate parameter ranges
        if (request.Temperature.HasValue && (request.Temperature < 0 || request.Temperature > 2))
        {
            return BadRequest(CreateErrorResponse("invalid_request_error", "Temperature must be between 0 and 2", "temperature"));
        }

        if (request.TopP.HasValue && (request.TopP <= 0 || request.TopP > 1))
        {
            return BadRequest(CreateErrorResponse("invalid_request_error", "Top-p must be between 0 and 1", "top_p"));
        }

        if (request.MaxCompletionTokens.HasValue && request.MaxCompletionTokens <= 0)
        {
            return BadRequest(CreateErrorResponse("invalid_request_error", "Max completion tokens must be greater than 0", "max_completion_tokens"));
        }

        try
        {
            // Check if the model exists
            var model = await _hostService.GetModelAsync(request.Model, cancellationToken);
            if (model == null)
            {
                return NotFound(CreateErrorResponse("model_not_found", $"Model '{request.Model}' not found", "model"));
            }

            if (model.IsLoaded == false)
            {
                return NotFound(CreateErrorResponse("model_not_found", $"Model '{request.Model}' is not loaded", "model"));
            }

            // Check if the model supports text generation
            if (!model.Capabilities.SupportsTextGeneration)
            {
                return BadRequest(CreateErrorResponse("invalid_request_error", $"Model '{request.Model}' does not support text generation", "model"));
            }

            // Convert OpenAI messages to prompt
            var generationRequest = _converterService.ConvertToGenerationRequest(request);

            // Handle streaming requests
            if (request.Stream == true)
            {
                return await CreateChatCompletionStream(request, generationRequest, cancellationToken);
            }

            // Generate text
            var generationResponse = await _hostService.GenerateTextAsync(request.Model, generationRequest, cancellationToken);

            // Convert to OpenAI response format
            var completionId = $"chatcmpl-{Guid.NewGuid():N}";
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var response = _converterService.ConvertToOpenAIResponse(generationResponse, request.Model, completionId, timestamp);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating chat completion with model {Model}", request.Model);
            return StatusCode(500, CreateErrorResponse("internal_error", $"Error generating chat completion: {ex.Message}"));
        }
    }

    /// <summary>
    /// Creates embeddings for the provided input (OpenAI-compatible)
    /// </summary>
    [HttpPost("embeddings")]
    public async Task<ActionResult<OpenAIEmbeddingResponse>> CreateEmbeddings(
        [FromBody] OpenAIEmbeddingRequest request,
        CancellationToken cancellationToken)
    {
        // Validate request object
        if (request == null)
        {
            return BadRequest(CreateErrorResponse("invalid_request_error", "Request body is required"));
        }

        // Validate request
        if (string.IsNullOrEmpty(request.Model))
        {
            return BadRequest(CreateErrorResponse("invalid_request_error", "Model is required", "model"));
        }

        if (request.Input == null)
        {
            return BadRequest(CreateErrorResponse("invalid_request_error", "Input is required", "input"));
        }

        try
        {
            // Check if the model exists
            var model = await _hostService.GetModelAsync(request.Model, cancellationToken);
            if (model == null)
            {
                return NotFound(CreateErrorResponse("model_not_found", $"Model '{request.Model}' not found", "model"));
            }

            if (model.IsLoaded == false)
            {
                return NotFound(CreateErrorResponse("model_not_found", $"Model '{request.Model}' is not loaded", "model"));
            }

            // Check if the model supports embeddings
            if (!model.Capabilities.SupportsEmbeddings)
            {
                // Some text generation models can be used for embeddings via hidden states
                // For now, return a more informative error
                return BadRequest(CreateErrorResponse("model_not_supported",
                    $"Model '{request.Model}' does not support embeddings. Only embedding-specific models are currently supported.",
                    "model"));
            }

            // Convert input to string array
            var embeddingRequest = _converterService.ConvertToEmbeddingRequest(request);

            // Generate embeddings
            var embeddingResponse = await _hostService.GenerateEmbeddingsAsync(request.Model, embeddingRequest, cancellationToken);

            // Convert to OpenAI response format
            var response = _converterService.ConvertToOpenAIEmbeddingResponse(embeddingResponse, request.Model);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating embeddings with model {Model}", request.Model);
            return StatusCode(500, CreateErrorResponse("internal_error", $"Error generating embeddings: {ex.Message}"));
        }
    }

    /// <summary>
    /// Handles streaming chat completions
    /// </summary>
    private async Task<ActionResult> CreateChatCompletionStream(
        OpenAIChatCompletionRequest request,
        GenerationRequest generationRequest,
        CancellationToken cancellationToken)
    {
        try
        {
            // Set streaming response headers
            Response.ContentType = "text/event-stream";
            Response.Headers.Add("Cache-Control", "no-cache");
            Response.Headers.Add("Connection", "keep-alive");

            var completionId = $"chatcmpl-{Guid.NewGuid():N}";
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // Stream the generated text
            await foreach (var token in _hostService.GenerateTextStreamAsync(request.Model, generationRequest, cancellationToken))
            {
                var streamResponse = new
                {
                    id = completionId,
                    @object = "chat.completion.chunk",
                    created = timestamp,
                    model = request.Model,
                    choices = new[]
                    {
                        new
                        {
                            index = 0,
                            delta = new { content = token },
                            finish_reason = (string?)null
                        }
                    }
                };

                var json = JsonSerializer.Serialize(streamResponse, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                await Response.WriteAsync($"data: {json}\n\n");
                await Response.Body.FlushAsync(cancellationToken);
            }

            // Send final chunk
            var finalResponse = new
            {
                id = completionId,
                @object = "chat.completion.chunk",
                created = timestamp,
                model = request.Model,
                choices = new[]
                {
                    new
                    {
                        index = 0,
                        delta = new { },
                        finish_reason = "stop"
                    }
                }
            };

            var finalJson = JsonSerializer.Serialize(finalResponse, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            await Response.WriteAsync($"data: {finalJson}\n\n");
            await Response.WriteAsync("data: [DONE]\n\n");
            await Response.Body.FlushAsync(cancellationToken);

            return new EmptyResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error streaming chat completion with model {Model}", request.Model);

            var errorResponse = CreateErrorResponse("internal_error", $"Error generating chat completion: {ex.Message}");
            var errorJson = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            await Response.WriteAsync($"data: {errorJson}\n\n");
            await Response.WriteAsync("data: [DONE]\n\n");

            return new EmptyResult();
        }
    }

    /// <summary>
    /// Creates an OpenAI-compatible error response
    /// </summary>
    private OpenAIErrorResponse CreateErrorResponse(string type, string message, string? param = null, string? code = null)
    {
        return new OpenAIErrorResponse
        {
            Error = new OpenAIError
            {
                Type = type,
                Message = message,
                Param = param,
                Code = code
            }
        };
    }
}