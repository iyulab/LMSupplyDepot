using LMSupplyDepots.Models;

namespace LMSupplyDepots.Interfaces;

/// <summary>
/// Generic interface for model repositories that provides model access and management
/// </summary>
public interface IModelRepository
{
    /// <summary>
    /// Gets a model by its identifier
    /// </summary>
    Task<LMModel?> GetModelAsync(string modelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists models with optional filtering and pagination
    /// </summary>
    Task<IReadOnlyList<LMModel>> ListModelsAsync(
        ModelType? type = null,
        string? searchTerm = null,
        int skip = 0,
        int take = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds or updates a model in the repository
    /// </summary>
    Task<LMModel> SaveModelAsync(LMModel model, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a model from the repository
    /// </summary>
    Task<bool> DeleteModelAsync(string modelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a model exists in the repository
    /// </summary>
    Task<bool> ExistsAsync(string modelId, CancellationToken cancellationToken = default);
}