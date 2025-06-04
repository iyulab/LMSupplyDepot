using LLama;
using LLama.Common;
using LMSupplyDepots.External.LLamaEngine.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace LMSupplyDepots.External.LLamaEngine.Services;

public interface ILLMService
{
    Task<string> InferAsync(
        string modelIdentifier,
        string prompt,
        InferenceParams? parameters = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> InferStreamAsync(
        string modelIdentifier,
        string prompt,
        InferenceParams? parameters = null,
        CancellationToken cancellationToken = default);

    Task<float[]> CreateEmbeddingAsync(
        string modelIdentifier,
        string text,
        bool normalize = true,
        CancellationToken cancellationToken = default);
}

public class LLMService : ILLMService, IAsyncDisposable
{
    private readonly ILogger<LLMService> _logger;
    private readonly ILLamaModelManager _modelManager;
    private readonly ILLamaBackendService _backendService;
    private readonly ConcurrentDictionary<string, ModelResources> _loadedModels = new();
    private readonly ConcurrentDictionary<string, LLamaContext> _contexts = new();
    private readonly ConcurrentDictionary<string, InteractiveExecutor> _executors = new();
    private readonly SemaphoreSlim _inferLock = new(1, 1);
    private bool _disposed;

    // Model/context/executor retry settings
    private const int MaxRetries = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    public LLMService(
        ILogger<LLMService> logger,
        ILLamaModelManager modelManager,
        ILLamaBackendService backendService)
    {
        _logger = logger;
        _modelManager = modelManager;
        _backendService = backendService;
        _modelManager.ModelStateChanged += OnModelStateChanged;
    }

    private void OnModelStateChanged(object? sender, ModelStateChangedEventArgs e)
    {
        if (e.NewState == LocalModelState.Unloading || e.NewState == LocalModelState.Unloaded)
        {
            CleanupModelResourcesAsync(e.ModelIdentifier).GetAwaiter().GetResult();
        }
    }

    public async Task<string> InferAsync(
        string modelIdentifier,
        string prompt,
        InferenceParams? parameters = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _inferLock.WaitAsync(cancellationToken);

            int retryCount = 0;
            while (true)
            {
                try
                {
                    ThrowIfDisposed();
                    var (executor, _) = await GetExecutorWithRetryAsync(modelIdentifier, cancellationToken);
                    parameters ??= ParameterFactory.NewInferenceParams();

                    var result = new System.Text.StringBuilder();
                    await foreach (var text in executor.InferAsync(prompt, parameters)
                        .WithCancellation(cancellationToken))
                    {
                        result.Append(text);
                    }
                    return result.ToString();
                }
                catch (Exception ex) when (
                    ex is not OperationCanceledException &&
                    ex is not ObjectDisposedException &&
                    retryCount < MaxRetries)
                {
                    retryCount++;
                    _logger.LogWarning(ex,
                        "Inference attempt {RetryCount} failed for model {ModelId}. Retrying...",
                        retryCount, modelIdentifier);

                    await CleanupFailedResourcesAsync(modelIdentifier);
                    await Task.Delay(RetryDelay * retryCount, cancellationToken);
                    continue;
                }
            }
        }
        finally
        {
            _inferLock.Release();
        }
    }

    public async IAsyncEnumerable<string> InferStreamAsync(
        string modelIdentifier,
        string prompt,
        InferenceParams? parameters = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await _inferLock.WaitAsync(cancellationToken);
        try
        {
            int retryCount = 0;
            IAsyncEnumerable<string>? stream = null;

            while (stream == null)
            {
                try
                {
                    ThrowIfDisposed();
                    var (executor, _) = await GetExecutorWithRetryAsync(modelIdentifier, cancellationToken);
                    parameters ??= ParameterFactory.NewInferenceParams();
                    stream = executor.InferAsync(prompt, parameters, cancellationToken);
                }
                catch (Exception ex) when (
                    ex is not OperationCanceledException &&
                    ex is not ObjectDisposedException &&
                    retryCount < MaxRetries)
                {
                    retryCount++;
                    _logger.LogWarning(ex,
                        "Streaming inference attempt {RetryCount} failed for model {ModelId}. Retrying...",
                        retryCount, modelIdentifier);

                    await CleanupFailedResourcesAsync(modelIdentifier);
                    await Task.Delay(RetryDelay * retryCount, cancellationToken);
                }
            }

            // Apply WithCancellation in foreach loop
            await foreach (var text in stream.WithCancellation(cancellationToken))
            {
                yield return text;
            }
        }
        finally
        {
            _inferLock.Release();
        }
    }

    public async Task<float[]> CreateEmbeddingAsync(
        string modelIdentifier,
        string text,
        bool normalize = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _inferLock.WaitAsync(cancellationToken);

            int retryCount = 0;
            while (true)
            {
                try
                {
                    ThrowIfDisposed();
                    var (_, modelParams) = await GetExecutorWithRetryAsync(modelIdentifier, cancellationToken);
                    modelParams.Embeddings = true;

                    if (!_loadedModels.TryGetValue(modelIdentifier, out var resources))
                    {
                        throw new InvalidOperationException($"Model resources not found for {modelIdentifier}");
                    }

                    using var embedder = new LLamaEmbedder(resources.Weights, modelParams);
                    var embeddings = await embedder.GetEmbeddings(text);

                    if (embeddings.Count == 0)
                    {
                        throw new InvalidOperationException("Failed to generate embeddings");
                    }

                    var result = embeddings[0];
                    if (normalize)
                    {
                        NormalizeInPlace(result);
                    }

                    return result;
                }
                catch (Exception ex) when (
                    ex is not OperationCanceledException &&
                    ex is not ObjectDisposedException &&
                    retryCount < MaxRetries)
                {
                    retryCount++;
                    _logger.LogWarning(ex,
                        "Embedding creation attempt {RetryCount} failed for model {ModelId}. Retrying...",
                        retryCount, modelIdentifier);

                    await CleanupFailedResourcesAsync(modelIdentifier);
                    await Task.Delay(RetryDelay * retryCount, cancellationToken);
                    continue;
                }
            }
        }
        finally
        {
            _inferLock.Release();
        }
    }

    private static void NormalizeInPlace(float[] vector)
    {
        float magnitude = (float)Math.Sqrt(vector.Sum(x => x * x));
        if (magnitude > 0)
        {
            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] /= magnitude;
            }
        }
    }

    private async Task<(InteractiveExecutor Executor, ModelParams Parameters)> GetExecutorWithRetryAsync(
        string modelIdentifier,
        CancellationToken cancellationToken)
    {
        int retryCount = 0;
        while (true)
        {
            try
            {
                return await GetExecutorAsync(modelIdentifier, cancellationToken);
            }
            catch (Exception ex) when (
                ex is not OperationCanceledException &&
                ex is not ObjectDisposedException &&
                retryCount < MaxRetries)
            {
                retryCount++;
                _logger.LogWarning(ex,
                    "Get executor attempt {RetryCount} failed for model {ModelId}. Retrying...",
                    retryCount, modelIdentifier);

                await Task.Delay(RetryDelay * retryCount, cancellationToken);
                continue;
            }
        }
    }

    private async Task CleanupFailedResourcesAsync(string modelIdentifier)
    {
        try
        {
            if (_executors.TryRemove(modelIdentifier, out _))
            {
                _logger.LogInformation("Removed failed executor for model {ModelId}", modelIdentifier);
            }

            if (_contexts.TryRemove(modelIdentifier, out var context))
            {
                try
                {
                    context.Dispose();
                    _logger.LogInformation("Disposed failed context for model {ModelId}", modelIdentifier);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disposing failed context for model {ModelId}", modelIdentifier);
                }
            }

            if (_loadedModels.TryRemove(modelIdentifier, out var resources))
            {
                try
                {
                    resources.Dispose();
                    _logger.LogInformation("Disposed failed resources for model {ModelId}", modelIdentifier);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disposing failed resources for model {ModelId}", modelIdentifier);
                }
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cleanup of failed resources for model {ModelId}", modelIdentifier);
        }
    }

    private async Task<(InteractiveExecutor Executor, ModelParams Parameters)> GetExecutorAsync(
        string modelIdentifier,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        modelIdentifier = _modelManager.NormalizeModelIdentifier(modelIdentifier);

        // Reuse existing executor if available
        if (_executors.TryGetValue(modelIdentifier, out var executor))
        {
            var modelInfo = await _modelManager.GetModelInfoAsync(modelIdentifier);
            if (modelInfo?.State == LocalModelState.Loaded)
            {
                var parameters = _backendService.GetOptimalModelParams(modelInfo.FullPath);
                return (executor, parameters);
            }
        }

        // Create new executor
        var info = await _modelManager.GetModelInfoAsync(modelIdentifier);
        if (info?.State != LocalModelState.Loaded)
        {
            throw new InvalidOperationException($"Model {modelIdentifier} is not loaded");
        }

        var weights = _modelManager.GetModelWeights(modelIdentifier);
        if (weights is null)
        {
            throw new InvalidOperationException($"Model weights not found for {modelIdentifier}");
        }

        var modelParams = _backendService.GetOptimalModelParams(info.FullPath);

        LLamaContext? context = null;
        try
        {
            // Try to create context
            context = new LLamaContext(weights, modelParams);

            if (!_contexts.TryAdd(modelIdentifier, context))
            {
                throw new InvalidOperationException($"Failed to add context for model {modelIdentifier}");
            }

            // Try to create executor
            executor = new InteractiveExecutor(context);

            var finalExecutor = _executors.GetOrAdd(modelIdentifier, executor);

            // If another thread created it first, don't use this context
            if (!ReferenceEquals(finalExecutor, executor))
            {
                context.Dispose();
                context = null; // Prevent double dispose
            }

            return (finalExecutor, modelParams);
        }
        catch (Exception ex)
        {
            // Cleanup resources
            if (context != null)
            {
                _contexts.TryRemove(modelIdentifier, out _);
                context.Dispose();
            }

            _logger.LogError(ex, "Failed to initialize executor for model {ModelIdentifier}", modelIdentifier);

            // Replace with InvalidOperationException without using RuntimeWrappedException
            if (ex is InvalidOperationException || ex.InnerException is InvalidOperationException)
            {
                throw new InvalidOperationException(
                    "Critical error initializing LLama execution engine. This may indicate " +
                    "incompatible model format or corrupted model file.", ex);
            }

            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        _disposed = true;
        _modelManager.ModelStateChanged -= OnModelStateChanged;
        _inferLock.Dispose();

        var exceptions = new List<Exception>();

        foreach (var modelId in _loadedModels.Keys)
        {
            try
            {
                await CleanupModelResourcesAsync(modelId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during cleanup of model {ModelId}", modelId);
                exceptions.Add(ex);
            }
        }

        _loadedModels.Clear();
        _contexts.Clear();
        _executors.Clear();

        if (exceptions.Count == 1)
            throw exceptions[0];
        else if (exceptions.Count > 1)
            throw new AggregateException("Multiple errors occurred during disposal", exceptions);

        GC.SuppressFinalize(this);
    }

    private async Task CleanupModelResourcesAsync(string modelId)
    {
        if (_executors.TryRemove(modelId, out _))
        {
            _logger.LogInformation("Removed executor for model {ModelId}", modelId);
        }

        if (_contexts.TryRemove(modelId, out var context))
        {
            try
            {
                context.Dispose();
                _logger.LogInformation("Disposed context for model {ModelId}", modelId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing context for model {ModelId}", modelId);
                throw;
            }
        }

        if (_loadedModels.TryRemove(modelId, out var resources))
        {
            try
            {
                resources.Dispose();
                _logger.LogInformation("Disposed resources for model {ModelId}", modelId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing resources for model {ModelId}", modelId);
                throw;
            }
        }

        await Task.CompletedTask;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(LLMService));
    }
}