using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using LMSupplyDepots.ModelHub.Repositories;
using LMSupplyDepots.Inference.Services;
using LMSupplyDepots.Inference.Adapters;
using System.Diagnostics;

namespace LMSupplyDepots.SDK;

/// <summary>
/// Extension methods for registering LMSupplyDepots SDK services
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds LMSupplyDepots SDK services to the service collection
    /// </summary>
    public static IServiceCollection AddLMSupplyDepotSDK(this IServiceCollection services, LMSupplyDepotOptions options)
    {

        // Register options
        services.TryAddSingleton(Microsoft.Extensions.Options.Options.Create(options));

        // Ensure models directory exists
        if (!Directory.Exists(options.DataPath))
        {
            Directory.CreateDirectory(options.DataPath);
        }

        // Configure ModelHub services
        ConfigureModelHubServices(services, options.DataPath);

        // Configure Inference services
        ConfigureInferenceServices(services);

        // Configure ModelLoader services
        ConfigureModelLoaderServices(services);

        // Register the main LMSupplyDepot class using factory to use existing DI services
        services.AddSingleton<LMSupplyDepot>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<LMSupplyDepot>>();
            // Use the internal constructor that takes existing service provider
            return new LMSupplyDepot(provider, options, logger);
        });

        return services;
    }

    /// <summary>
    /// Configures ModelHub services
    /// </summary>
    private static void ConfigureModelHubServices(IServiceCollection services, string modelsPath)
    {

        // Remove any existing registrations that might conflict
        var descriptorsToRemove = services.Where(d =>
            d.ServiceType == typeof(IOptions<ModelHubOptions>) ||
            d.ServiceType == typeof(IModelRepository) ||
            d.ServiceType == typeof(FileSystemModelRepository)).ToList();

        foreach (var descriptor in descriptorsToRemove)
        {
            services.Remove(descriptor);
        }

        // Register ModelHubOptions with correct path
        services.AddSingleton<IOptions<ModelHubOptions>>(provider =>
        {
            return Microsoft.Extensions.Options.Options.Create(new ModelHubOptions
            {
                DataPath = modelsPath
            });
        });

        // Register model repository with explicit DataPath injection
        services.AddSingleton<IModelRepository>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<FileSystemModelRepository>>();
            var options = Microsoft.Extensions.Options.Options.Create(new ModelHubOptions
            {
                DataPath = modelsPath
            });
            return new FileSystemModelRepository(options, logger);
        });

        services.AddSingleton<FileSystemModelRepository>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<FileSystemModelRepository>>();
            var options = Microsoft.Extensions.Options.Options.Create(new ModelHubOptions
            {
                DataPath = modelsPath
            });
            return new FileSystemModelRepository(options, logger);
        });

        // Register ModelHub services for download management
        services.TryAddSingleton<LMSupplyDepots.ModelHub.Services.DownloadManager>();

        // Register downloaders
        services.TryAddSingleton<LMSupplyDepots.ModelHub.Interfaces.IModelDownloader, LMSupplyDepots.ModelHub.HuggingFace.HuggingFaceDownloader>();

        // Register ModelManager
        services.TryAddSingleton<LMSupplyDepots.ModelHub.Interfaces.IModelManager, LMSupplyDepots.ModelHub.Services.ModelManager>();

    }

    /// <summary>
    /// Configures Inference services
    /// </summary>
    private static void ConfigureInferenceServices(IServiceCollection services)
    {

        // Add LLama backend if available
        AddLLamaBackend(services);

    }

    /// <summary>
    /// Adds LLama backend services
    /// </summary>
    private static void AddLLamaBackend(IServiceCollection services)
    {

        try
        {
            // Add LLama engine services
            services.AddLLamaEngine();

            // Register LLama adapter
            services.AddSingleton<BaseModelAdapter, LLamaAdapter>();

        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SDK ServiceCollectionExtensions] Error adding LLama backend: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Configures ModelLoader services
    /// </summary>
    private static void ConfigureModelLoaderServices(IServiceCollection services)
    {

        // Register model loader service
        services.TryAddSingleton<IModelLoader>(serviceProvider =>
        {

            var modelRepository = serviceProvider.GetRequiredService<IModelRepository>();
            var logger = serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<RepositoryModelLoaderService>>();
            var adapters = serviceProvider.GetServices<BaseModelAdapter>();

            var adapterList = adapters.ToList();

            var service = new RepositoryModelLoaderService(modelRepository, logger, adapterList);

            // Initialize the service to reset all model states to unloaded
            // This ensures that after service restart, models aren't incorrectly marked as loaded
            try
            {
                service.InitializeAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SDK ServiceCollectionExtensions] Error initializing RepositoryModelLoaderService: {ex.Message}");
            }

            return service;
        });

    }
}
