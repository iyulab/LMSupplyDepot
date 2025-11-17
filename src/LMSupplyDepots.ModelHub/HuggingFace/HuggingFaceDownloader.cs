using LMSupplyDepots.External.HuggingFace.Client;

namespace LMSupplyDepots.ModelHub.HuggingFace;

/// <summary>
/// Implementation of IModelDownloader for Hugging Face models
/// </summary>
public partial class HuggingFaceDownloader : IModelDownloader, IDisposable
{
    private readonly HuggingFaceDownloaderOptions _options;
    private readonly ModelHubOptions _hubOptions;
    private readonly ILogger<HuggingFaceDownloader> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly FileSystemModelRepository _fileSystemRepository;
    private readonly Lazy<HuggingFaceClient> _client;
    private bool _disposed;

    private static readonly Regex _sourceIdRegex = new(@"^(hf|huggingface):(.+)$", RegexOptions.IgnoreCase);

    public string SourceName => "HuggingFace";

    /// <summary>
    /// Initializes a new instance of the HuggingFaceDownloader
    /// </summary>
    public HuggingFaceDownloader(
        IOptions<HuggingFaceDownloaderOptions> options,
        IOptions<ModelHubOptions> hubOptions,
        ILogger<HuggingFaceDownloader> logger,
        ILoggerFactory loggerFactory,
        FileSystemModelRepository fileSystemRepository)
    {
        _options = options.Value;
        _hubOptions = hubOptions.Value;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _fileSystemRepository = fileSystemRepository;

        _client = new Lazy<HuggingFaceClient>(() => CreateClient());
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
    /// Creates a HuggingFaceClient with the configured options
    /// </summary>
    private HuggingFaceClient CreateClient()
    {
        var clientOptions = new HuggingFaceClientOptions
        {
            Token = string.IsNullOrWhiteSpace(_options.ApiToken) ? null : _options.ApiToken,
            MaxConcurrentDownloads = _options.MaxConcurrentFileDownloads,
            Timeout = _options.RequestTimeout,
            MaxRetries = _options.MaxRetries
        };

        if (string.IsNullOrWhiteSpace(_options.ApiToken))
        {
            _logger.LogInformation("HuggingFace API token not provided. Only public models will be accessible.");
        }
        else
        {
            _logger.LogInformation("HuggingFace API token configured. Public and private models will be accessible.");
        }

        return new HuggingFaceClient(clientOptions, _loggerFactory);
    }

    /// <summary>
    /// Gets the model directory path for a model identifier
    /// </summary>
    internal string GetModelDirectoryPath(ModelIdentifier identifier)
    {
        return FileSystemHelper.GetModelDirectoryPath(identifier, _hubOptions.GetModelsDirectory());
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
                if (_client.IsValueCreated)
                {
                    _client.Value.Dispose();
                }
            }

            _disposed = true;
        }
    }
}