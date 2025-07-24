using LMSupplyDepots.External.LLamaEngine.Services;
using LMSupplyDepots.SDK.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LMSupplyDepots.External.LLamaEngine.Extensions;

/// <summary>
/// Service collection extensions for LLamaEngine metadata services
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add LLamaEngine model metadata services to the service collection
    /// </summary>
    public static IServiceCollection AddLlamaModelMetadata(this IServiceCollection services)
    {
        services.AddSingleton<ModelMetadataExtractor>();
        services.AddSingleton<DynamicChatTemplateService>();
        services.AddSingleton<LlamaModelMetadataService>();

        // Register as the IModelMetadataService interface
        services.AddSingleton<IModelMetadataService>(provider =>
            provider.GetRequiredService<LlamaModelMetadataService>());

        return services;
    }
}
