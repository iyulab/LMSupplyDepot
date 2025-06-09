using LMSupplyDepots.ModelHub.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LMSupplyDepots.ModelHub;

/// <summary>
/// Extension methods for registering ModelHub services
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds ModelHub services to the service collection
    /// </summary>
    public static IServiceCollection AddModelHub(this IServiceCollection services, Action<ModelHubOptions>? configureOptions = null)
    {
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.TryAddSingleton(Microsoft.Extensions.Options.Options.Create(new ModelHubOptions()));
        }

        services.TryAddSingleton<FileSystemModelRepository>();
        services.TryAddSingleton<IModelRepository>(sp => sp.GetRequiredService<FileSystemModelRepository>());
        services.TryAddSingleton<DownloadManager>();
        services.TryAddSingleton<IModelManager, ModelManager>();

        return services;
    }

    /// <summary>
    /// Adds a specific model downloader to the service collection
    /// </summary>
    public static IServiceCollection AddModelDownloader<TDownloader>(this IServiceCollection services)
        where TDownloader : class, IModelDownloader
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelDownloader, TDownloader>());
        return services;
    }
}