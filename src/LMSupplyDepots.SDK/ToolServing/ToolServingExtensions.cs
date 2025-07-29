using LMSupplyDepots.SDK.ToolServing;
using LMSupplyDepots.SDK.ToolServing.Bridges;
using LMSupplyDepots.SDK.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LMSupplyDepots.SDK.ToolServing.Extensions;

/// <summary>
/// Extension methods for registering tool serving services
/// </summary>
public static class ToolServingServiceCollectionExtensions
{
    /// <summary>
    /// Add tool serving capabilities to the service collection
    /// </summary>
    public static IServiceCollection AddToolServing(
        this IServiceCollection services,
        Action<ToolServingOptions>? configureOptions = null)
    {
        // Configure options
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.Configure<ToolServingOptions>(options => { });
        }

        // Register core tool serving services
        services.AddSingleton<IToolRegistry, InMemoryToolRegistry>();
        services.AddScoped<IToolExecutionEngine, ToolExecutionEngine>();

        // Register orchestration bridges
        services.AddScoped<IToolOrchestrationBridge, YoMoToolBridge>();
        services.AddScoped<OpenAIFunctionsAdapter>();
        services.AddScoped<MCPToolServer>();

        // Register default tools
        services.AddSingleton<IFunctionTool, GetWeatherTool>();
        services.AddSingleton<IFunctionTool, CalculatorTool>();

        return services;
    }

    /// <summary>
    /// Add YoMo bridge capabilities
    /// </summary>
    public static IServiceCollection AddYoMoBridge(
        this IServiceCollection services,
        Action<YoMoBridgeOptions>? configureOptions = null)
    {
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        services.AddScoped<YoMoToolBridge>();
        return services;
    }

    /// <summary>
    /// Add MCP server capabilities
    /// </summary>
    public static IServiceCollection AddMCPServer(
        this IServiceCollection services,
        Action<MCPBridgeOptions>? configureOptions = null)
    {
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        services.AddScoped<MCPToolServer>();
        return services;
    }

    /// <summary>
    /// Register a custom tool
    /// </summary>
    public static IServiceCollection AddTool<T>(this IServiceCollection services)
        where T : class, IFunctionTool
    {
        services.AddSingleton<IFunctionTool, T>();
        return services;
    }

    /// <summary>
    /// Register a custom tool with factory
    /// </summary>
    public static IServiceCollection AddTool<T>(
        this IServiceCollection services,
        Func<IServiceProvider, T> factory)
        where T : class, IFunctionTool
    {
        services.AddSingleton<IFunctionTool>(factory);
        return services;
    }
}

/// <summary>
/// Tool serving configuration builder
/// </summary>
public class ToolServingBuilder
{
    public IServiceCollection Services { get; }

    public ToolServingBuilder(IServiceCollection services)
    {
        Services = services;
    }

    /// <summary>
    /// Configure tool serving options
    /// </summary>
    public ToolServingBuilder Configure(Action<ToolServingOptions> configureOptions)
    {
        Services.Configure(configureOptions);
        return this;
    }

    /// <summary>
    /// Add a custom tool registry
    /// </summary>
    public ToolServingBuilder UseToolRegistry<T>() where T : class, IToolRegistry
    {
        Services.AddSingleton<IToolRegistry, T>();
        return this;
    }

    /// <summary>
    /// Add a custom execution engine
    /// </summary>
    public ToolServingBuilder UseExecutionEngine<T>() where T : class, IToolExecutionEngine
    {
        Services.AddScoped<IToolExecutionEngine, T>();
        return this;
    }

    /// <summary>
    /// Add YoMo bridge
    /// </summary>
    public ToolServingBuilder WithYoMoBridge(Action<YoMoBridgeOptions>? configure = null)
    {
        Services.AddYoMoBridge(configure);
        return this;
    }

    /// <summary>
    /// Add MCP server
    /// </summary>
    public ToolServingBuilder WithMCPServer(Action<MCPBridgeOptions>? configure = null)
    {
        Services.AddMCPServer(configure);
        return this;
    }

    /// <summary>
    /// Add a tool
    /// </summary>
    public ToolServingBuilder AddTool<T>() where T : class, IFunctionTool
    {
        Services.AddTool<T>();
        return this;
    }

    /// <summary>
    /// Add a tool with factory
    /// </summary>
    public ToolServingBuilder AddTool<T>(Func<IServiceProvider, T> factory)
        where T : class, IFunctionTool
    {
        Services.AddTool(factory);
        return this;
    }
}

/// <summary>
/// Tool serving extensions for LMSupplyDepot
/// </summary>
public static class LMSupplyDepotToolServingExtensions
{
    /// <summary>
    /// Add enhanced tool serving to LMSupplyDepot
    /// </summary>
    public static LMSupplyDepot ConfigureToolServing(
        this LMSupplyDepot supplyDepot,
        Action<ToolServingBuilder> configure)
    {
        var builder = new ToolServingBuilder(new ServiceCollection());
        configure(builder);

        // In a real implementation, you'd need access to the internal service collection
        // This is a placeholder for the concept

        return supplyDepot;
    }

    /// <summary>
    /// Register tools automatically on startup
    /// </summary>
    public static Task<LMSupplyDepot> RegisterDefaultToolsAsync(this LMSupplyDepot supplyDepot)
    {
        // Get tool registry from service provider
        // In a real implementation, you'd access the actual service provider

        // Example registration:
        // var toolRegistry = serviceProvider.GetRequiredService<IToolRegistry>();
        // var weatherTool = new GetWeatherTool();
        // await toolRegistry.RegisterToolAsync(weatherTool, new ToolMetadata
        // {
        //     Name = weatherTool.Definition.Name,
        //     Description = "Built-in weather tool",
        //     Tags = new List<string> { "weather", "builtin" }
        // });

        return Task.FromResult(supplyDepot);
    }
}

/// <summary>
/// Tool serving initialization helper
/// </summary>
public static class ToolServingInitializer
{
    /// <summary>
    /// Initialize tool serving with default configuration
    /// </summary>
    public static async Task<IServiceProvider> InitializeToolServingAsync(
        IServiceCollection services,
        ToolServingOptions? options = null)
    {
        // Add tool serving with options
        services.AddToolServing(opts =>
        {
            if (options != null)
            {
                opts.EnableHttpServer = options.EnableHttpServer;
                opts.HttpPort = options.HttpPort;
                opts.EnableGrpcServer = options.EnableGrpcServer;
                opts.GrpcPort = options.GrpcPort;
                opts.EnableDiscovery = options.EnableDiscovery;
                opts.EnableMetrics = options.EnableMetrics;
                opts.EnableTracing = options.EnableTracing;
                opts.MaxConcurrentExecutions = options.MaxConcurrentExecutions;
                opts.ExecutionTimeout = options.ExecutionTimeout;
                opts.Bridges = options.Bridges;
            }
        });

        var serviceProvider = services.BuildServiceProvider();

        // Auto-register default tools
        await RegisterDefaultToolsAsync(serviceProvider);

        return serviceProvider;
    }

    /// <summary>
    /// Register default tools
    /// </summary>
    private static async Task RegisterDefaultToolsAsync(IServiceProvider serviceProvider)
    {
        var toolRegistry = serviceProvider.GetRequiredService<IToolRegistry>();
        var tools = serviceProvider.GetServices<IFunctionTool>();

        foreach (var tool in tools)
        {
            var metadata = new ToolMetadata
            {
                Name = tool.Definition.Name,
                Description = tool.Definition.Description ?? "Default tool",
                Tags = new List<string> { "builtin", "default" },
                Protocol = "direct",
                Runtime = "csharp"
            };

            await toolRegistry.RegisterToolAsync(tool, metadata);
        }
    }
}
