using LMSupplyDepots.External.LLamaEngine.Services;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLLamaEngine(this IServiceCollection services)
    {
        services.AddSingleton<ILLamaBackendService, LLamaBackendService>();
        services.AddSingleton<ILLamaModelManager, LLamaModelManager>();
        services.AddSingleton<ILLMService, LLMService>();
        services.AddSystemMonitoring();

        return services;
    }

    public static IServiceCollection AddSystemMonitoring(this IServiceCollection services)
    {
        services.AddSingleton<ISystemMonitorService, SystemMonitorService>();
        return services;
    }
}
