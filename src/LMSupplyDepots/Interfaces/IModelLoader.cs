using LMSupplyDepots.Models;

namespace LMSupplyDepots.Interfaces;

/// <summary>
/// Interface for loading and unloading models.
/// </summary>
public interface IModelLoader
{
    /// <summary>
    /// Loads a model into memory.
    /// </summary>
    Task<LMModel> LoadModelAsync(
        string modelId,
        Dictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Unloads a model from memory.
    /// </summary>
    Task UnloadModelAsync(
        string modelId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a model is currently loaded.
    /// </summary>
    Task<bool> IsModelLoadedAsync(
        string modelId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a list of currently loaded models.
    /// </summary>
    Task<IReadOnlyList<LMModel>> GetLoadedModelsAsync(
        CancellationToken cancellationToken = default);
}