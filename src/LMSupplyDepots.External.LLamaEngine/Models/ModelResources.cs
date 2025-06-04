using LLama;

namespace LMSupplyDepots.External.LLamaEngine.Models;

public class ModelResources(LLamaWeights weights) : IDisposable
{
    private readonly object _lock = new();
    private bool _disposed;

    public LLamaWeights Weights { get; } = weights;

    public void Dispose()
    {
        if (_disposed) return;

        lock (_lock)
        {
            if (_disposed) return;

            try
            {
                Weights.Dispose();
            }
            finally
            {
                _disposed = true;
            }
        }

        GC.SuppressFinalize(this);
    }
}