using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LMSupplyDepots.ModelHub.HuggingFace;

/// <summary>
/// Extension methods for registering HuggingFace downloader services
/// </summary>
public static class HuggingFaceExtensions
{
    /// <summary>
    /// Adds HuggingFace model downloader to the service collection
    /// </summary>
    public static IServiceCollection AddHuggingFaceDownloader(
        this IServiceCollection services,
        Action<HuggingFaceDownloaderOptions>? configureOptions = null)
    {
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.TryAddOptions<HuggingFaceDownloaderOptions>(new HuggingFaceDownloaderOptions());
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IModelDownloader, HuggingFaceDownloader>());
        return services;
    }

    /// <summary>
    /// Helper method to add options if not already configured
    /// </summary>
    private static IServiceCollection TryAddOptions<TOptions>(
        this IServiceCollection services,
        TOptions options)
        where TOptions : class, new()
    {
        services.TryAddSingleton(Microsoft.Extensions.Options.Options.Create(options));
        return services;
    }
}