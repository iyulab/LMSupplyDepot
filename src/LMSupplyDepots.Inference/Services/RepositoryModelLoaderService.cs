using LMSupplyDepots.Inference.Configuration;

namespace LMSupplyDepots.Inference.Services;

/// <summary>
/// Service for loading and unloading models that uses IModelRepository
/// </summary>
public class RepositoryModelLoaderService : ModelLoaderService
{
    private readonly IModelRepository _modelRepository;

    /// <summary>
    /// Initializes a new instance of the RepositoryModelLoaderService
    /// </summary>
    public RepositoryModelLoaderService(
        ILogger<ModelLoaderService> logger,
        IOptionsMonitor<InferenceOptions> options,
        IEnumerable<Adapters.BaseModelAdapter> adapters,
        IModelRepository modelRepository)
        : base(logger, options, adapters)
    {
        _modelRepository = modelRepository;
    }

    /// <summary>
    /// Gets a model by its ID using the IModelRepository
    /// </summary>
    protected override async Task<LMModel?> GetModelAsync(string modelId, CancellationToken cancellationToken)
    {
        return await _modelRepository.GetModelAsync(modelId, cancellationToken);
    }
}