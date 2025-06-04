using LMSupplyDepots.Contracts;
using LMSupplyDepots.External.LLamaEngine.Services;

namespace LMSupplyDepots.Inference.Engines.Embedding;

/// <summary>
/// Embedding engine implementation using LLama
/// </summary>
public class LLamaEmbeddingEngine : BaseEmbeddingEngine
{
    private readonly ILLMService _llmService;
    private readonly string _modelId;
    private readonly Dictionary<string, object?> _parameters;

    /// <summary>
    /// The engine name
    /// </summary>
    public override string EngineName => "LLama";

    /// <summary>
    /// Initializes a new instance of the LLama embedding engine
    /// </summary>
    public LLamaEmbeddingEngine(
        ILogger<LLamaEmbeddingEngine> logger,
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
    /// Implementation of embedding generation logic
    /// </summary>
    protected override async Task<List<float[]>> GenerateEmbeddingsInternalAsync(
        EmbeddingRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        try
        {
            var embeddings = new List<float[]>();

            // Process each text
            foreach (var text in request.Texts)
            {
                // Pass normalize=false as we handle normalization at the BaseEmbeddingEngine level
                var embedding = await _llmService.CreateEmbeddingAsync(
                    _modelId,
                    text,
                    normalize: false,
                    cancellationToken);

                embeddings.Add(embedding);
            }

            return embeddings;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during embedding generation for model {ModelId}", _modelId);
            throw new GenerationException(_modelId, "Embedding generation failed", ex);
        }
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