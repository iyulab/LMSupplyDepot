using LMSupplyDepots.Host;
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
        // Register options
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.TryAddSingleton(Microsoft.Extensions.Options.Options.Create(new LMSupplyDepotOptions()));
        }

        // Register host service
        services.TryAddSingleton<IHostService, HostService>();

        return services;
    }
}