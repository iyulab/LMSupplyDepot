using Microsoft.Extensions.Logging.Abstractions;

namespace LMSupplyDepots.SDK;

/// <summary>
/// Main entry point for LMSupplyDepots SDK
/// </summary>
public partial class LMSupplyDepot : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;
    private readonly LMSupplyDepotOptions _options;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the LMSupplyDepot with default options
    /// </summary>
    public LMSupplyDepot() : this(new LMSupplyDepotOptions())
    {
    }

    /// <summary>
    /// Initializes a new instance of the LMSupplyDepot with custom options
    /// </summary>
    public LMSupplyDepot(LMSupplyDepotOptions options) : this(options, NullLoggerFactory.Instance)
    {
    }

    /// <summary>
    /// Initializes a new instance of the LMSupplyDepot with custom options and logger factory
    /// </summary>
    public LMSupplyDepot(LMSupplyDepotOptions options, ILoggerFactory loggerFactory)
    {
        _options = options ?? new LMSupplyDepotOptions();
        _logger = loggerFactory.CreateLogger<LMSupplyDepot>();

        // Create service collection
        var services = new ServiceCollection();

        // Add logging
        services.AddLogging(builder =>
        {
            builder.AddProvider(new ForwardingLoggerProvider(loggerFactory));
        });

        // Configure services
        ConfigureServices(services);

        // Build service provider
        _serviceProvider = services.BuildServiceProvider();

        _logger.LogInformation("LMSupplyDepot initialized with models directory: {ModelsDirectory}",
            _options.DataPath);
    }

    /// <summary>
    /// Creates an LMSupplyDepot with a custom service provider
    /// </summary>
    internal LMSupplyDepot(IServiceProvider serviceProvider, LMSupplyDepotOptions options, ILogger logger)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Configures services for the LMSupplyDepot
    /// </summary>
    private void ConfigureServices(IServiceCollection services)
    {
        // Ensure models directory exists
        if (!Directory.Exists(_options.DataPath))
        {
            Directory.CreateDirectory(_options.DataPath);
        }

        // Configure ModelHub services
        ConfigureModelHubServices(services, _options.DataPath);

        // Configure Inference services
        ConfigureInferenceServices(services);

        // Configure ModelLoader services
        ConfigureModelLoaderServices(services);
    }

    /// <summary>
    /// Disposes resources
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Protected implementation of Dispose pattern
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose service provider if it implements IDisposable
                if (_serviceProvider is IDisposable disposable)
                {
                    disposable.Dispose();
                }

                // Clean up text generation engines
                foreach (var engine in _textGenerationEngines.Values)
                {
                    if (engine is IDisposable disposableEngine)
                    {
                        disposableEngine.Dispose();
                    }
                }
                _textGenerationEngines.Clear();

                // Clean up embedding engines
                foreach (var engine in _embeddingEngines.Values)
                {
                    if (engine is IDisposable disposableEngine)
                    {
                        disposableEngine.Dispose();
                    }
                }
                _embeddingEngines.Clear();
            }

            _disposed = true;
        }
    }

    /// <summary>
    /// Logger provider that forwards to another logger factory
    /// </summary>
    private class ForwardingLoggerProvider : ILoggerProvider
    {
        private readonly ILoggerFactory _loggerFactory;

        public ForwardingLoggerProvider(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return _loggerFactory.CreateLogger(categoryName);
        }

        public void Dispose()
        {
            // Don't dispose the original factory
        }
    }
}