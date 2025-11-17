using LMSupplyDepots.External.HuggingFace.Client;

namespace LMSupplyDepots.ModelHub.HuggingFace;

/// <summary>
/// Implementation of IModelDownloader for Hugging Face models
/// Refactored to use dependency injection for testability
/// </summary>
public partial class HuggingFaceDownloader : IModelDownloader, IDisposable
{
    private readonly HuggingFaceDownloaderOptions _options;
    private readonly ModelHubOptions _hubOptions;
    private readonly ILogger<HuggingFaceDownloader> _logger;
    private readonly FileSystemModelRepository _fileSystemRepository;
    private readonly IHuggingFaceClient _client;
    private bool _disposed;

    private static readonly Regex _sourceIdRegex = new(@"^(hf|huggingface):(.+)$", RegexOptions.IgnoreCase);

    public string SourceName => "HuggingFace";

    /// <summary>
    /// Initializes a new instance of the HuggingFaceDownloader with dependency injection
    /// </summary>
    public HuggingFaceDownloader(
        IOptions<HuggingFaceDownloaderOptions> options,
        IOptions<ModelHubOptions> hubOptions,
        ILogger<HuggingFaceDownloader> logger,
        IHuggingFaceClient client,
        FileSystemModelRepository fileSystemRepository)
    {
        _options = options.Value;
        _hubOptions = hubOptions.Value;
        _logger = logger;
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _fileSystemRepository = fileSystemRepository;
    }

    /// <summary>
    /// Legacy constructor for backward compatibility - creates client internally
    /// </summary>
    [Obsolete("Use the constructor with IHuggingFaceClient injection for better testability")]
    public HuggingFaceDownloader(
        IOptions<HuggingFaceDownloaderOptions> options,
        IOptions<ModelHubOptions> hubOptions,
        ILogger<HuggingFaceDownloader> logger,
        ILoggerFactory loggerFactory,
        FileSystemModelRepository fileSystemRepository)
        : this(options, hubOptions, logger, CreateClient(options.Value, loggerFactory), fileSystemRepository)
    {
    }

    /// <summary>
    /// Determines if this downloader can handle the given source ID
    /// </summary>
    public bool CanHandle(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
            return false;

        if (_sourceIdRegex.IsMatch(sourceId))
            return true;

        if (sourceId.Contains('/'))
            return true;

        return false;
    }

    /// <summary>
    /// Creates a HuggingFaceClient with the configured options (static factory method)
    /// </summary>
    private static HuggingFaceClient CreateClient(HuggingFaceDownloaderOptions options, ILoggerFactory loggerFactory)
    {
        var clientOptions = new HuggingFaceClientOptions
        {
            Token = string.IsNullOrWhiteSpace(options.ApiToken) ? null : options.ApiToken,
            MaxConcurrentDownloads = options.MaxConcurrentFileDownloads,
            Timeout = options.RequestTimeout,
            MaxRetries = options.MaxRetries
        };

        var logger = loggerFactory?.CreateLogger<HuggingFaceDownloader>();
        if (string.IsNullOrWhiteSpace(options.ApiToken))
        {
            logger?.LogInformation("HuggingFace API token not provided. Only public models will be accessible.");
        }
        else
        {
            logger?.LogInformation("HuggingFace API token configured. Public and private models will be accessible.");
        }

        return new HuggingFaceClient(clientOptions, loggerFactory);
    }

    /// <summary>
    /// Gets the model directory path for a model identifier
    /// </summary>
    internal string GetModelDirectoryPath(ModelIdentifier identifier)
    {
        return FileSystemHelper.GetModelDirectoryPath(identifier, _hubOptions.ModelsDirectory);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _client?.Dispose();
            }

            _disposed = true;
        }
    }
}