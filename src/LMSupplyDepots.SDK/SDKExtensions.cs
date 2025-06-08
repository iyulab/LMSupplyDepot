namespace LMSupplyDepots.SDK;

/// <summary>
/// Utility extensions for resolving model keys
/// </summary>
internal static class SDKExtensions
{
    /// <summary>
    /// Resolves a model key (ID or alias) to the actual model ID
    /// </summary>
    internal static async Task<string> ResolveModelKeyAsync(
        this IModelManager modelManager,
        string modelKey,
        CancellationToken cancellationToken = default)
    {
        var model = await modelManager.GetModelAsync(modelKey, cancellationToken);
        if (model != null)
        {
            return model.Id;
        }

        model = await modelManager.GetModelByAliasAsync(modelKey, cancellationToken);
        if (model != null)
        {
            return model.Id;
        }

        return modelKey;
    }

    /// <summary>
    /// Resolves a model key for repository operations
    /// </summary>
    internal static async Task<string> ResolveModelKeyAsync(
        this IModelRepository repository,
        string modelKey,
        CancellationToken cancellationToken = default)
    {
        var model = await repository.GetModelAsync(modelKey, cancellationToken);
        if (model != null)
        {
            return model.Id;
        }

        var models = await repository.ListModelsAsync(
            null, null, 0, int.MaxValue, cancellationToken);

        var matchedModel = models.FirstOrDefault(m =>
            !string.IsNullOrEmpty(m.Alias) &&
            string.Equals(m.Alias, modelKey, StringComparison.OrdinalIgnoreCase));

        if (matchedModel != null)
        {
            return matchedModel.Id;
        }

        return modelKey;
    }
}