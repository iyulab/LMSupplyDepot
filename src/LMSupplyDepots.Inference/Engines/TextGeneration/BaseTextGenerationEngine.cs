using LMSupplyDepots.Contracts;

namespace LMSupplyDepots.Inference.Engines.TextGeneration;

/// <summary>
/// Base class for text generation engines
/// </summary>
public abstract class BaseTextGenerationEngine : ITextGenerationEngine, IDisposable
{
    protected readonly ILogger _logger;
    protected readonly SemaphoreSlim _inferLock;
    protected bool _disposed;

    /// <summary>
    /// The engine name
    /// </summary>
    public abstract string EngineName { get; }

    /// <summary>
    /// Initializes a new instance of a text generation engine
    /// </summary>
    protected BaseTextGenerationEngine(ILogger logger, int maxConcurrentOperations = 1)
    {
        _logger = logger;
        _inferLock = new SemaphoreSlim(maxConcurrentOperations, maxConcurrentOperations);
    }

    /// <summary>
    /// Generates text based on the provided request
    /// </summary>
    public virtual async Task<GenerationResponse> GenerateAsync(
        GenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await _inferLock.WaitAsync(cancellationToken);

            var tokenCount = EstimateTokenCount(request.Prompt);

            var response = await GenerateTextAsync(request, cancellationToken);

            stopwatch.Stop();
            response.ElapsedTime = stopwatch.Elapsed;
            response.PromptTokens = tokenCount;
            response.OutputTokens = EstimateTokenCount(response.Text);

            return response;
        }
        finally
        {
            _inferLock.Release();
        }
    }

    /// <summary>
    /// Streams generated text tokens as they are produced
    /// </summary>
    public virtual async IAsyncEnumerable<string> GenerateStreamAsync(
        GenerationRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        await _inferLock.WaitAsync(cancellationToken);
        try
        {
            await foreach (var token in GenerateTextStreamAsync(request, cancellationToken))
            {
                yield return token;
            }
        }
        finally
        {
            _inferLock.Release();
        }
    }

    /// <summary>
    /// Implementation of text generation logic
    /// </summary>
    protected abstract Task<GenerationResponse> GenerateTextAsync(
        GenerationRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Implementation of streaming text generation logic
    /// </summary>
    protected abstract IAsyncEnumerable<string> GenerateTextStreamAsync(
        GenerationRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Validates a generation request
    /// </summary>
    protected virtual void ValidateRequest(GenerationRequest request)
    {
        if (string.IsNullOrEmpty(request.Prompt))
        {
            throw new ArgumentException("Prompt cannot be empty", nameof(request));
        }

        if (request.MaxTokens <= 0)
        {
            throw new ArgumentException("MaxTokens must be positive", nameof(request));
        }

        if (request.Temperature < 0 || request.Temperature > 2)
        {
            throw new ArgumentException("Temperature must be between 0 and 2", nameof(request));
        }

        if (request.TopP <= 0 || request.TopP > 1)
        {
            throw new ArgumentException("TopP must be between 0 and 1", nameof(request));
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
                _inferLock.Dispose();
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