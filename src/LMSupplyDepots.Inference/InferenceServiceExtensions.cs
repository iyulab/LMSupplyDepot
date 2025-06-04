using LMSupplyDepots.Inference.Adapters;
using LMSupplyDepots.Inference.Configuration;
using LMSupplyDepots.Inference.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LMSupplyDepots.Inference;

/// <summary>
/// Extension methods for adding inference services to the dependency injection container
/// </summary>
public static class ServiceExtensions
{
    /// <summary>
    /// Adds core inference services to the service collection
    /// </summary>
    public static IServiceCollection AddInferenceServices(
        this IServiceCollection services,
        Action<InferenceOptions>? configureOptions = null)
    {
        // Register options
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.TryAddSingleton(Microsoft.Extensions.Options.Options.Create(new InferenceOptions()));
        }

        // Register core services
        services.TryAddSingleton<TokenizerService>();
        services.TryAddSingleton<ModelStateService>();

        // Register ModelLoaderService as an abstract interface
        // (concrete implementation should be provided by hosting application)
        services.TryAddSingleton<IModelLoader, ModelLoaderService>();

        return services;
    }

    /// <summary>
    /// Adds LLama backend services to the service collection
    /// </summary>
    public static IServiceCollection AddLLamaBackend(this IServiceCollection services)
    {
        // Add LLama engine services
        services.AddLLamaEngine();

        // Register the LLama adapter
        services.TryAddEnumerable(ServiceDescriptor.Singleton<BaseModelAdapter, LLamaAdapter>());

        return services;
    }

    /// <summary>
    /// Adds a custom model adapter to the service collection
    /// </summary>
    public static IServiceCollection AddModelAdapter<TAdapter>(this IServiceCollection services)
        where TAdapter : BaseModelAdapter
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<BaseModelAdapter, TAdapter>());
        return services;
    }
}