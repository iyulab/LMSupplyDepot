using LMSupplyDepots.Host;
using LMSupplyDepots.Host.Services;
using LMSupplyDepots.SDK;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LMSupplyDepots.Host;

/// <summary>
/// Extension methods for registering LMSupplyDepots Host services
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds LMSupplyDepots Host services to the service collection
    /// </summary>
    public static IServiceCollection AddLMSupplyDepots(this IServiceCollection services, Action<LMSupplyDepotOptions>? configureOptions = null)
    {
        // Configure options
        var options = new LMSupplyDepotOptions();
        if (configureOptions != null)
        {
            configureOptions(options);
            services.Configure(configureOptions);
        }
        else
        {
            services.TryAddSingleton(Microsoft.Extensions.Options.Options.Create(options));
        }

        // Register SDK services using the SDK extension method
        services.AddLMSupplyDepotSDK(options);

        // Register host services
        services.TryAddSingleton<IHostService, HostService>();
        services.TryAddSingleton<IToolExecutionService, ToolExecutionService>();

        return services;
    }
}