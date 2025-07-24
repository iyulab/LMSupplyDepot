using LLama.Common;
using LMSupplyDepots.Contracts;
using LMSupplyDepots.External.LLamaEngine.Services;

namespace LMSupplyDepots.Inference.Engines.TextGeneration;

/// <summary>
/// Text generation engine implementation using LLama
/// </summary>
public class LLamaTextGenerationEngine : BaseTextGenerationEngine
{
    private readonly ILLMService _llmService;
    private readonly string _modelId;
    private readonly Dictionary<string, object?> _parameters;

    /// <summary>
    /// The engine name
    /// </summary>
    public override string EngineName => "LLama";

    /// <summary>
    /// Initializes a new instance of the LLama text generation engine
    /// </summary>
    public LLamaTextGenerationEngine(
        ILogger<LLamaTextGenerationEngine> logger,
        ILLMService llmService,
        string modelId,
        Dictionary<string, object?> parameters)
        : base(logger)
    {
        _llmService = llmService;
        _modelId = modelId;
        _parameters = parameters;
    }

    /// <summary>
    /// Implementation of text generation logic
    /// </summary>
    protected override async Task<GenerationResponse> GenerateTextAsync(
        GenerationRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        try
        {
            // Set up the prompt
            var prompt = request.Prompt;

            // Create inference parameters
            var inferenceParams = CreateInferenceParams(request);

            _logger.LogDebug("Starting text generation for model {ModelId} with temp: {Temperature}, topP: {TopP}, tokens: {MaxTokens}",
                _modelId, request.Temperature, request.TopP, request.MaxTokens);

            // Call the LLM service
            var result = await _llmService.InferAsync(
                _modelId,
                prompt,
                inferenceParams,
                cancellationToken);

            return new GenerationResponse
            {
                Text = result,
                FinishReason = "stop"
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Text generation was cancelled for model {ModelId}", _modelId);
            return new GenerationResponse
            {
                Text = string.Empty,
                FinishReason = "cancelled"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during text generation for model {ModelId}", _modelId);
            throw new GenerationException(_modelId, "Text generation failed", ex);
        }
    }

    /// <summary>
    /// Implementation of streaming text generation logic
    /// </summary>
    protected override async IAsyncEnumerable<string> GenerateTextStreamAsync(
        GenerationRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        // Set up the prompt
        var prompt = request.Prompt;

        // Create inference parameters
        var inferenceParams = CreateInferenceParams(request);

        _logger.LogDebug("Starting streaming text generation for model {ModelId}", _modelId);

        // Get the async enumerable stream
        IAsyncEnumerable<string> stream;
        try
        {
            stream = _llmService.InferStreamAsync(
                _modelId,
                prompt,
                inferenceParams,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Streaming text generation was cancelled for model {ModelId}", _modelId);
            yield break;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during streaming text generation for model {ModelId}", _modelId);
            throw new GenerationException(_modelId, "Text generation streaming failed", ex);
        }

        // Enumerate the stream outside the try-catch
        await foreach (var token in stream.WithCancellation(cancellationToken))
        {
            yield return token;
        }
    }

    /// <summary>
    /// Creates inference parameters from a generation request
    /// </summary>
    private InferenceParams CreateInferenceParams(GenerationRequest request)
    {
        // Get anti-prompt strings from request parameters
        string[]? antiPrompt = null;
        if (request.Parameters?.TryGetValue("antiprompt", out var antiPromptObj) == true &&
            antiPromptObj is IEnumerable<string> antiPromptStrs)
        {
            antiPrompt = antiPromptStrs.ToArray();
        }

        // Get repeat penalty from request parameters or use instance parameters as fallback
        float repeatPenalty = 1.1f;
        if (request.Parameters?.TryGetValue("repeat_penalty", out var repeatPenaltyObj) == true &&
            repeatPenaltyObj is float rpValue)
        {
            repeatPenalty = rpValue;
        }
        else if (_parameters.TryGetValue("repeat_penalty", out var instanceRpObj) &&
                 instanceRpObj is float instanceRpValue)
        {
            repeatPenalty = instanceRpValue;
        }

        // Create inference parameters using the factory
        var inferenceParams = LMSupplyDepots.External.LLamaEngine.ParameterFactory.NewInferenceParams(
            maxTokens: request.MaxTokens,
            antiprompt: antiPrompt,
            temperature: request.Temperature,
            topP: request.TopP,
            repeatPenalty: repeatPenalty
        );

        return inferenceParams;
    }

    /// <summary>
    /// Disposes resources
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        // No additional resources to dispose
        base.Dispose(disposing);
    }
}