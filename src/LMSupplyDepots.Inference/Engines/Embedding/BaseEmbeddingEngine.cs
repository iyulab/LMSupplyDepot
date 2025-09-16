using LMSupplyDepots.Contracts;
using LMSupplyDepots.Inference.Utils;

namespace LMSupplyDepots.Inference.Engines.Embedding;

/// <summary>
/// Base class for embedding engines
/// </summary>
public abstract class BaseEmbeddingEngine : IEmbeddingEngine, IDisposable
{
    protected readonly ILogger _logger;
    protected readonly SemaphoreSlim _embedLock;
    protected bool _disposed;

    /// <summary>
    /// The engine name
    /// </summary>
    public abstract string EngineName { get; }

    /// <summary>
    /// Initializes a new instance of an embedding engine
    /// </summary>
    protected BaseEmbeddingEngine(ILogger logger, int maxConcurrentOperations = 1)
    {
        _logger = logger;
        _embedLock = new SemaphoreSlim(maxConcurrentOperations, maxConcurrentOperations);
    }

    /// <summary>
    /// Generates embeddings for the provided texts
    /// </summary>
    public virtual async Task<EmbeddingResponse> GenerateEmbeddingsAsync(
        EmbeddingRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await _embedLock.WaitAsync(cancellationToken);

            var tokenCount = request.Texts.Sum(EstimateTokenCount);

            var embeddings = await GenerateEmbeddingsInternalAsync(request, cancellationToken);

            // Normalize if requested
            if (request.Normalize)
            {
                NormalizeEmbeddings(embeddings);
            }

            stopwatch.Stop();

            return new EmbeddingResponse
            {
                Embeddings = embeddings,
                TotalTokens = tokenCount,
                ElapsedTime = stopwatch.Elapsed
            };
        }
        finally
        {
            _embedLock.Release();
        }
    }

    /// <summary>
    /// Implementation of embedding generation logic
    /// </summary>
    protected abstract Task<List<float[]>> GenerateEmbeddingsInternalAsync(
        EmbeddingRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Validates an embedding request
    /// </summary>
    protected virtual void ValidateRequest(EmbeddingRequest request)
    {
        if (request.Texts == null || request.Texts.Count == 0)
        {
            throw new ArgumentException("Texts cannot be empty", nameof(request));
        }

        if (request.Texts.Any(string.IsNullOrEmpty))
        {
            throw new ArgumentException("Texts cannot contain empty strings", nameof(request));
        }
    }

    /// <summary>
    /// Normalizes a list of embeddings in-place
    /// </summary>
    protected virtual void NormalizeEmbeddings(List<float[]> embeddings)
    {
        foreach (var embedding in embeddings)
        {
            NormalizeEmbedding(embedding);
        }
    }

    /// <summary>
    /// Normalizes a single embedding vector to unit length in-place
    /// </summary>
    protected virtual void NormalizeEmbedding(float[] embedding)
    {
        float sumSquared = 0.0f;
        for (int i = 0; i < embedding.Length; i++)
        {
            sumSquared += embedding[i] * embedding[i];
        }

        if (sumSquared > 0)
        {
            float magnitude = (float)Math.Sqrt(sumSquared);
            for (int i = 0; i < embedding.Length; i++)
            {
                embedding[i] /= magnitude;
            }
        }
    }

    /// <summary>
    /// Estimate token count for a text
    /// </summary>
    protected virtual int EstimateTokenCount(string text)
    {
        // Simple estimation, can be overridden by engines with proper tokenizers
        return LMSupplyDepots.Utils.TokenizationHelpers.EstimateTokenCount(text);
    }

    /// <summary>
    /// Disposes resources
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Protected implementation of Dispose pattern
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _embedLock.Dispose();
            }

            _disposed = true;
        }
    }

    /// <summary>
    /// Throws if the engine is disposed
    /// </summary>
    protected void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, GetType().FullName ?? GetType().Name);
    }
}