using LMSupplyDepots.External.LLamaEngine.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LMSupplyDepots.External.LLamaEngine.Extensions;

/// <summary>
/// Service collection extensions for LLamaEngine
/// </summary>
public static class LlamaEngineExtensions
{
    /// <summary>
    /// Add LLamaEngine services to the service collection
    /// </summary>
    public static IServiceCollection AddLLamaEngine(this IServiceCollection services)
    {
        services.AddSingleton<ILLamaBackendService, LLamaBackendService>();
        services.AddSingleton<ILLamaModelManager, LLamaModelManager>();
        services.AddSingleton<ILLMService, LLMService>();
        services.AddSingleton<LLamaBackendService>();
        services.AddSingleton<ModelConfigurationService>();
        services.AddSingleton<SystemMonitorService>();

        return services;
    }
}
