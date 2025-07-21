using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using LMSupplyDepots.ModelHub;
using LMSupplyDepots.ModelHub.Repositories;
using LMSupplyDepots.Inference;
using LMSupplyDepots.Inference.Services;
using LMSupplyDepots.Inference.Adapters;
using LMSupplyDepots.External.LLamaEngine;

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
        Console.WriteLine("[SDK ServiceCollectionExtensions] AddLMSupplyDepotSDK called");

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
            Console.WriteLine("[SDK ServiceCollectionExtensions] Creating LMSupplyDepot with existing DI services");
            var logger = provider.GetRequiredService<ILogger<LMSupplyDepot>>();
            // Use the internal constructor that takes existing service provider
            return new LMSupplyDepot(provider, options, logger);
        });

        Console.WriteLine("[SDK ServiceCollectionExtensions] All SDK services registered");
        return services;
    }

    /// <summary>
    /// Configures ModelHub services
    /// </summary>
    private static void ConfigureModelHubServices(IServiceCollection services, string modelsPath)
    {
        Console.WriteLine($"[SDK ServiceCollectionExtensions] Configuring ModelHub services with path: {modelsPath}");

        // Remove any existing registrations that might conflict
        var descriptorsToRemove = services.Where(d =>
            d.ServiceType == typeof(IOptions<ModelHubOptions>) ||
            d.ServiceType == typeof(IModelRepository) ||
            d.ServiceType == typeof(FileSystemModelRepository)).ToList();

        foreach (var descriptor in descriptorsToRemove)
        {
            services.Remove(descriptor);
            Console.WriteLine($"[SDK ServiceCollectionExtensions] Removed existing registration: {descriptor.ServiceType.Name}");
        }

        // Register ModelHubOptions with correct path
        services.AddSingleton<IOptions<ModelHubOptions>>(provider =>
        {
            Console.WriteLine($"[SDK ServiceCollectionExtensions] Creating ModelHubOptions with DataPath: {modelsPath}");
            return Microsoft.Extensions.Options.Options.Create(new ModelHubOptions
            {
                DataPath = modelsPath
            });
        });

        // Register model repository with explicit DataPath injection
        services.AddSingleton<IModelRepository>(provider =>
        {
            Console.WriteLine($"[SDK ServiceCollectionExtensions] Creating FileSystemModelRepository with DataPath: {modelsPath}");
            var logger = provider.GetRequiredService<ILogger<FileSystemModelRepository>>();
            var options = Microsoft.Extensions.Options.Options.Create(new ModelHubOptions
            {
                DataPath = modelsPath
            });
            return new FileSystemModelRepository(options, logger);
        });

        services.AddSingleton<FileSystemModelRepository>(provider =>
        {
            Console.WriteLine($"[SDK ServiceCollectionExtensions] Creating concrete FileSystemModelRepository with DataPath: {modelsPath}");
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

        Console.WriteLine("[SDK ServiceCollectionExtensions] ModelHub services configured");
    }

    /// <summary>
    /// Configures Inference services
    /// </summary>
    private static void ConfigureInferenceServices(IServiceCollection services)
    {
        Console.WriteLine("[SDK ServiceCollectionExtensions] Configuring Inference services");

        // Add LLama backend if available
        AddLLamaBackend(services);

        Console.WriteLine("[SDK ServiceCollectionExtensions] Inference services configured");
    }

    /// <summary>
    /// Adds LLama backend services
    /// </summary>
    private static void AddLLamaBackend(IServiceCollection services)
    {
        Console.WriteLine("[SDK ServiceCollectionExtensions] Adding LLama backend");

        try
        {
            // Add LLama engine services
            services.AddLLamaEngine();

            // Register LLama adapter
            services.AddSingleton<BaseModelAdapter, LLamaAdapter>();

            Console.WriteLine("[SDK ServiceCollectionExtensions] LLama backend added successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SDK ServiceCollectionExtensions] Error adding LLama backend: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Configures ModelLoader services
    /// </summary>
    private static void ConfigureModelLoaderServices(IServiceCollection services)
    {
        Console.WriteLine("[SDK ServiceCollectionExtensions] Configuring ModelLoader services");

        // Register model loader service
        services.TryAddSingleton<IModelLoader>(serviceProvider =>
        {
            Console.WriteLine("[SDK ServiceCollectionExtensions] Creating RepositoryModelLoaderService");

            var modelRepository = serviceProvider.GetRequiredService<IModelRepository>();
            var logger = serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<RepositoryModelLoaderService>>();
            var adapters = serviceProvider.GetServices<BaseModelAdapter>();

            var adapterList = adapters.ToList();
            Console.WriteLine($"[SDK ServiceCollectionExtensions] Found {adapterList.Count} adapters during service creation:");
            foreach (var adapter in adapterList)
            {
                Console.WriteLine($"[SDK ServiceCollectionExtensions] - {adapter.GetType().Name}: {adapter.AdapterName}");
            }

            var service = new RepositoryModelLoaderService(modelRepository, logger, adapterList);

            // Initialize the service to reset all model states to unloaded
            // This ensures that after service restart, models aren't incorrectly marked as loaded
            try
            {
                service.InitializeAsync().GetAwaiter().GetResult();
                Console.WriteLine("[SDK ServiceCollectionExtensions] RepositoryModelLoaderService initialized - all models reset to unloaded state");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SDK ServiceCollectionExtensions] Warning: Failed to initialize RepositoryModelLoaderService: {ex.Message}");
            }

            return service;
        });

        Console.WriteLine("[SDK ServiceCollectionExtensions] ModelLoader services configured");
    }
}
