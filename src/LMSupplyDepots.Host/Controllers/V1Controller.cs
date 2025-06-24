using LMSupplyDepots.Contracts;
using LMSupplyDepots.Host.Models.OpenAI;
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
                Data = loadedModels.Select(m => new OpenAIModel
                {
                    Id = m.Key, // Use Key (alias if available, otherwise Id)
                    Created = timestamp,
                    OwnedBy = "local",
                    Type = GetModelTypeString(m.Type)
                }).ToList()
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
        // Validate request
        if (string.IsNullOrEmpty(request.Model))
        {
            return BadRequest(CreateErrorResponse("invalid_request_error", "Model is required", "model"));
        }

        if (request.Messages == null || request.Messages.Count == 0)
        {
            return BadRequest(CreateErrorResponse("invalid_request_error", "Messages are required", "messages"));
        }

        try
        {
            // Check if the model exists
            var model = await _hostService.GetModelAsync(request.Model, cancellationToken);
            if (model == null)
            {
                return NotFound(CreateErrorResponse("model_not_found", $"Model '{request.Model}' not found", "model"));
            }

            // Check if the model supports text generation
            if (!model.Capabilities.SupportsTextGeneration)
            {
                return BadRequest(CreateErrorResponse("invalid_request_error", $"Model '{request.Model}' does not support text generation", "model"));
            }

            // Convert OpenAI messages to prompt
            var prompt = ConvertMessagesToPrompt(request.Messages);

            // Convert to internal generation request
            var generationRequest = new GenerationRequest
            {
                Model = request.Model,
                Prompt = prompt,
                MaxTokens = request.MaxTokens ?? 256,
                Temperature = request.Temperature ?? 0.7f,
                TopP = request.TopP ?? 0.95f,
                Stream = request.Stream
            };

            // Handle streaming requests
            if (request.Stream)
            {
                return await CreateChatCompletionStream(request, generationRequest, cancellationToken);
            }

            // Generate text
            var generationResponse = await _hostService.GenerateTextAsync(request.Model, generationRequest, cancellationToken);

            // Convert to OpenAI response format
            var response = new OpenAIChatCompletionResponse
            {
                Id = $"chatcmpl-{Guid.NewGuid():N}",
                Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Model = request.Model,
                Choices = new List<OpenAIChatChoice>
                {
                    new OpenAIChatChoice
                    {
                        Index = 0,
                        Message = new OpenAIChatMessage
                        {
                            Role = "assistant",
                            Content = generationResponse.Text
                        },
                        FinishReason = ConvertFinishReason(generationResponse.FinishReason)
                    }
                },
                Usage = new OpenAIUsage
                {
                    PromptTokens = generationResponse.PromptTokens,
                    CompletionTokens = generationResponse.OutputTokens,
                    TotalTokens = generationResponse.PromptTokens + generationResponse.OutputTokens
                }
            };

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

            // Check if the model supports embeddings
            if (!model.Capabilities.SupportsEmbeddings)
            {
                return BadRequest(CreateErrorResponse("invalid_request_error", $"Model '{request.Model}' does not support embeddings", "model"));
            }

            // Convert input to string array
            var texts = ConvertInputToTexts(request.Input);
            if (texts.Count == 0)
            {
                return BadRequest(CreateErrorResponse("invalid_request_error", "Input must contain at least one text", "input"));
            }

            // Convert to internal embedding request
            var embeddingRequest = new EmbeddingRequest
            {
                Model = request.Model,
                Texts = texts,
                Normalize = false
            };

            // Generate embeddings
            var embeddingResponse = await _hostService.GenerateEmbeddingsAsync(request.Model, embeddingRequest, cancellationToken);

            // Convert to OpenAI response format
            var response = new OpenAIEmbeddingResponse
            {
                Model = request.Model,
                Data = embeddingResponse.Embeddings.Select((embedding, index) => new OpenAIEmbeddingData
                {
                    Index = index,
                    Embedding = embedding
                }).ToList(),
                Usage = new OpenAIUsage
                {
                    PromptTokens = embeddingResponse.TotalTokens,
                    TotalTokens = embeddingResponse.TotalTokens
                }
            };

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
    /// Converts OpenAI messages to a single prompt string
    /// </summary>
    private string ConvertMessagesToPrompt(List<OpenAIChatMessage> messages)
    {
        var promptParts = new List<string>();

        foreach (var message in messages)
        {
            switch (message.Role.ToLowerInvariant())
            {
                case "system":
                    promptParts.Add($"System: {message.Content}");
                    break;
                case "user":
                    promptParts.Add($"User: {message.Content}");
                    break;
                case "assistant":
                    promptParts.Add($"Assistant: {message.Content}");
                    break;
                default:
                    promptParts.Add($"{message.Role}: {message.Content}");
                    break;
            }
        }

        return string.Join("\n\n", promptParts) + "\n\nAssistant:";
    }

    /// <summary>
    /// Converts OpenAI input to list of texts
    /// </summary>
    private List<string> ConvertInputToTexts(object input)
    {
        var texts = new List<string>();

        if (input is string singleText)
        {
            texts.Add(singleText);
        }
        else if (input is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.String)
            {
                texts.Add(jsonElement.GetString() ?? string.Empty);
            }
            else if (jsonElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in jsonElement.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        texts.Add(item.GetString() ?? string.Empty);
                    }
                }
            }
        }
        else if (input is IEnumerable<string> stringArray)
        {
            texts.AddRange(stringArray);
        }

        return texts;
    }

    /// <summary>
    /// Converts internal finish reason to OpenAI format
    /// </summary>
    private string? ConvertFinishReason(string finishReason)
    {
        return finishReason.ToLowerInvariant() switch
        {
            "length" => "length",
            "stop" => "stop",
            "eos" => "stop",
            "" => "stop",
            _ => "stop"
        };
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

    /// <summary>
    /// Converts model type enum to OpenAI-compatible string
    /// </summary>
    private static string GetModelTypeString(LMSupplyDepots.Models.ModelType modelType)
    {
        return modelType switch
        {
            LMSupplyDepots.Models.ModelType.TextGeneration => "text-generation",
            LMSupplyDepots.Models.ModelType.Embedding => "embedding",
            _ => "unknown"
        };
    }
}