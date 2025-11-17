using LMSupplyDepots.External.HuggingFace.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace LMSupplyDepots.ModelHub.HuggingFace;

/// <summary>
/// Extension methods for registering HuggingFace downloader services
/// Refactored to support dependency injection for testability
/// </summary>
public static class HuggingFaceExtensions
{
    /// <summary>
    /// Adds HuggingFace model downloader to the service collection with DI support
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

        // Register IHuggingFaceClient as a factory to create properly configured instances
        services.TryAddSingleton<IHuggingFaceClient>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<HuggingFaceDownloaderOptions>>().Value;
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            var clientOptions = new HuggingFaceClientOptions
            {
                Token = string.IsNullOrWhiteSpace(options.ApiToken) ? null : options.ApiToken,
                MaxConcurrentDownloads = options.MaxConcurrentFileDownloads,
                Timeout = options.RequestTimeout,
                MaxRetries = options.MaxRetries
            };

            return new HuggingFaceClient(clientOptions, loggerFactory);
        });

        // Register HuggingFaceDownloader with injected IHuggingFaceClient
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