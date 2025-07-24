using LMSupplyDepots.SDK.OpenAI.Models;
using LMSupplyDepots.SDK.OpenAI.Services;
using LMSupplyDepots.SDK.Tools;

namespace LMSupplyDepots.SDK;

/// <summary>
/// OpenAI compatibility functionality for LMSupplyDepot
/// </summary>
public partial class LMSupplyDepot
{
    /// <summary>
    /// Gets the OpenAI converter service
    /// </summary>
    private IOpenAIConverterService OpenAIConverter => _serviceProvider.GetRequiredService<IOpenAIConverterService>();

    /// <summary>
    /// Gets the tool service
    /// </summary>
    private IToolService ToolService => _serviceProvider.GetRequiredService<IToolService>();

    #region OpenAI Compatible Methods

    /// <summary>
    /// Lists available models in OpenAI-compatible format (only loaded models)
    /// </summary>
    public async Task<OpenAIModelsResponse> ListModelsOpenAIAsync(CancellationToken cancellationToken = default)
    {
        var models = await GetLoadedModelsAsync(cancellationToken);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var openAIModels = models.Select(model => OpenAIConverter.ConvertToOpenAIModel(model, timestamp)).ToList();

        return new OpenAIModelsResponse { Data = openAIModels };
    }

    /// <summary>
    /// Creates a chat completion (OpenAI-compatible)
    /// </summary>
    public async Task<OpenAIChatCompletionResponse> CreateChatCompletionAsync(
        OpenAIChatCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        // Validate request
        ValidateChatCompletionRequest(request);

        // Check if the model exists and is loaded
        var model = await GetModelAsync(request.Model, cancellationToken);
        if (model == null)
        {
            throw new ArgumentException($"Model '{request.Model}' not found", nameof(request));
        }

        if (!model.IsLoaded)
        {
            throw new InvalidOperationException($"Model '{request.Model}' is not loaded");
        }

        // Check if the model supports text generation
        if (!model.Capabilities.SupportsTextGeneration)
        {
            throw new InvalidOperationException($"Model '{request.Model}' does not support text generation");
        }

        // Convert OpenAI request to internal format
        var generationRequest = OpenAIConverter.ConvertToGenerationRequest(request);

        // Generate text
        var generationResponse = await GenerateTextAsync(request.Model, generationRequest, cancellationToken);

        // Convert to OpenAI response format
        var completionId = $"chatcmpl-{Guid.NewGuid():N}";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var response = OpenAIConverter.ConvertToOpenAIResponse(generationResponse, request.Model, completionId, timestamp);

        return response;
    }

    /// <summary>
    /// Creates embeddings for the provided input (OpenAI-compatible)
    /// </summary>
    public async Task<OpenAIEmbeddingResponse> CreateEmbeddingsAsync(
        OpenAIEmbeddingRequest request,
        CancellationToken cancellationToken = default)
    {
        // Validate request
        ValidateEmbeddingRequest(request);

        // Check if the model exists and is loaded
        var model = await GetModelAsync(request.Model, cancellationToken);
        if (model == null)
        {
            throw new ArgumentException($"Model '{request.Model}' not found", nameof(request));
        }

        if (!model.IsLoaded)
        {
            throw new InvalidOperationException($"Model '{request.Model}' is not loaded");
        }

        // Check if the model supports embeddings
        if (!model.Capabilities.SupportsEmbeddings)
        {
            throw new InvalidOperationException($"Model '{request.Model}' does not support embeddings");
        }

        // Convert OpenAI request to internal format
        var embeddingRequest = OpenAIConverter.ConvertToEmbeddingRequest(request);

        // Generate embeddings
        var embeddingResponse = await GenerateEmbeddingsAsync(request.Model, embeddingRequest, cancellationToken);

        // Convert to OpenAI response format
        var response = OpenAIConverter.ConvertToOpenAIEmbeddingResponse(embeddingResponse, request.Model);

        return response;
    }

    /// <summary>
    /// Creates a streaming chat completion (OpenAI-compatible)
    /// </summary>
    public async IAsyncEnumerable<string> CreateChatCompletionStreamAsync(
        OpenAIChatCompletionRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Validate request
        ValidateChatCompletionRequest(request);

        // Check if the model exists and is loaded
        var model = await GetModelAsync(request.Model, cancellationToken);
        if (model == null)
        {
            throw new ArgumentException($"Model '{request.Model}' not found", nameof(request));
        }

        if (!model.IsLoaded)
        {
            throw new InvalidOperationException($"Model '{request.Model}' is not loaded");
        }

        // Check if the model supports text generation
        if (!model.Capabilities.SupportsTextGeneration)
        {
            throw new InvalidOperationException($"Model '{request.Model}' does not support text generation");
        }

        // Convert OpenAI request to internal format
        var generationRequest = OpenAIConverter.ConvertToGenerationRequest(request);
        generationRequest.Stream = true;

        // Generate streaming text
        await foreach (var chunk in GenerateTextStreamAsync(
            request.Model,
            generationRequest.Prompt,
            generationRequest.MaxTokens,
            generationRequest.Temperature,
            generationRequest.TopP,
            generationRequest.Parameters,
            cancellationToken))
        {
            // Format as OpenAI streaming response
            var completionId = $"chatcmpl-{Guid.NewGuid():N}";
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

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
                        delta = new { content = chunk },
                        finish_reason = (string?)null // Cannot determine if complete from string chunk
                    }
                }
            };

            yield return System.Text.Json.JsonSerializer.Serialize(streamResponse);
        }
    }

    #endregion

    #region Tools Support

    /// <summary>
    /// Register a function tool
    /// </summary>
    public void RegisterTool(IFunctionTool tool)
    {
        ToolService.RegisterTool(tool);
    }

    /// <summary>
    /// Get all registered tools as OpenAI tool definitions
    /// </summary>
    public List<Tool> GetAvailableTools()
    {
        return ToolService.GetAvailableTools();
    }

    /// <summary>
    /// Execute a tool call
    /// </summary>
    public async Task<string> ExecuteToolAsync(string toolName, string argumentsJson, CancellationToken cancellationToken = default)
    {
        return await ToolService.ExecuteToolAsync(toolName, argumentsJson, cancellationToken);
    }

    /// <summary>
    /// Check if a tool is available
    /// </summary>
    public bool IsToolAvailable(string toolName)
    {
        return ToolService.IsToolAvailable(toolName);
    }

    #endregion

    #region Private Validation Methods

    private static void ValidateChatCompletionRequest(OpenAIChatCompletionRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        if (string.IsNullOrEmpty(request.Model))
            throw new ArgumentException("Model is required", nameof(request));

        if (request.Messages == null || request.Messages.Count == 0)
            throw new ArgumentException("Messages are required", nameof(request));

        // Validate messages
        foreach (var message in request.Messages)
        {
            if (string.IsNullOrEmpty(message.Role))
                throw new ArgumentException("Message role is required", nameof(request));

            // Content is required for most roles except assistant with tool_calls
            if (message.Content == null &&
                (message.Role.ToLowerInvariant() != "assistant" || message.ToolCalls == null || message.ToolCalls.Count == 0))
            {
                throw new ArgumentException("Message content is required", nameof(request));
            }

            // Validate role values
            var validRoles = new[] { "system", "user", "assistant", "tool", "developer" };
            if (!validRoles.Contains(message.Role.ToLowerInvariant()))
            {
                throw new ArgumentException($"Invalid message role '{message.Role}'. Must be one of: {string.Join(", ", validRoles)}", nameof(request));
            }

            // Validate tool message requirements
            if (message.Role.ToLowerInvariant() == "tool" && string.IsNullOrEmpty(message.ToolCallId))
            {
                throw new ArgumentException("Tool messages must include tool_call_id", nameof(request));
            }
        }

        // Validate parameter ranges
        if (request.Temperature.HasValue && (request.Temperature < 0 || request.Temperature > 2))
            throw new ArgumentOutOfRangeException(nameof(request), "Temperature must be between 0 and 2");

        if (request.TopP.HasValue && (request.TopP <= 0 || request.TopP > 1))
            throw new ArgumentOutOfRangeException(nameof(request), "Top-p must be between 0 and 1");

        if (request.MaxCompletionTokens.HasValue && request.MaxCompletionTokens <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Max completion tokens must be greater than 0");
    }

    private static void ValidateEmbeddingRequest(OpenAIEmbeddingRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        if (string.IsNullOrEmpty(request.Model))
            throw new ArgumentException("Model is required", nameof(request));

        if (request.Input == null)
            throw new ArgumentException("Input is required", nameof(request));
    }

    #endregion
}
