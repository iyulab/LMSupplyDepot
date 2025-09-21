using LMSupplyDepots.SDK.OpenAI.Services;
using LMSupplyDepots.SDK.Tools;
using LMSupplyDepots.External.LLamaEngine.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LMSupplyDepots.SDK.Extensions;

/// <summary>
/// Extension methods for configuring LMSupplyDepots SDK services
/// </summary>
public static class OpenAIServiceCollectionExtensions
{
    /// <summary>
    /// Adds OpenAI compatibility services to the service collection
    /// </summary>
    public static IServiceCollection AddOpenAICompatibility(this IServiceCollection services)
    {
        // Add stop token optimizer for advanced stop token processing
        services.AddScoped<IStopTokenOptimizer, StopTokenOptimizer>();

        // Add OpenAI converter service with stop token optimizer dependency
        services.AddScoped<IOpenAIConverterService, OpenAIConverterService>();

        return services;
    }

    /// <summary>
    /// Adds tools support services to the service collection
    /// </summary>
    public static IServiceCollection AddToolsSupport(this IServiceCollection services)
    {
        services.AddSingleton<IToolService, ToolService>();

        return services;
    }

    /// <summary>
    /// Adds built-in example tools to the service collection
    /// </summary>
    public static IServiceCollection AddBuiltInTools(this IServiceCollection services)
    {
        services.AddSingleton<IFunctionTool, GetWeatherTool>();
        services.AddSingleton<IFunctionTool, CalculatorTool>();

        return services;
    }
}
