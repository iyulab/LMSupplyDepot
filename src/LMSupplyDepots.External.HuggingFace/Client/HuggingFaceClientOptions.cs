namespace LMSupplyDepots.External.HuggingFace.Client;

/// <summary>
/// Represents configuration options for the HuggingFaceClient.
/// </summary>
public class HuggingFaceClientOptions
{
    private const int DefaultMaxConcurrentDownloads = 5;
    private const int DefaultProgressUpdateInterval = 100;
    private const int DefaultBufferSize = 8192;
    private const int DefaultMaxRetries = 1;
    private const int DefaultRetryDelayMilliseconds = 1000;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    private int _maxConcurrentDownloads = DefaultMaxConcurrentDownloads;
    private int _progressUpdateInterval = DefaultProgressUpdateInterval;
    private int _bufferSize = DefaultBufferSize;
    private TimeSpan _timeout = DefaultTimeout;
    private int _maxRetries = DefaultMaxRetries;
    private int _retryDelayMilliseconds = DefaultRetryDelayMilliseconds;

    /// <summary>
    /// Gets or sets the authentication token for the Hugging Face API.
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of concurrent downloads allowed (1-20).
    /// </summary>
    public int MaxConcurrentDownloads
    {
        get => _maxConcurrentDownloads;
        set
        {
            const int minValue = 1;
            const int maxValue = 20;

            if (value < minValue || value > maxValue)
            {
                throw new ArgumentException(
                    $"Value must be between {minValue} and {maxValue} (current: {value})",
                    nameof(MaxConcurrentDownloads));
            }
            _maxConcurrentDownloads = value;
        }
    }

    /// <summary>
    /// Gets or sets the interval in milliseconds between progress updates (50-5000ms).
    /// </summary>
    public int ProgressUpdateInterval
    {
        get => _progressUpdateInterval;
        set
        {
            const int minValue = 50;
            const int maxValue = 5000;

            if (value < minValue || value > maxValue)
            {
                throw new ArgumentException(
                    $"Value must be between {minValue}ms and {maxValue}ms (current: {value}ms)",
                    nameof(ProgressUpdateInterval));
            }
            _progressUpdateInterval = value;
        }
    }

    /// <summary>
    /// Gets or sets the timeout duration for HTTP requests (10s-30min).
    /// </summary>
    public TimeSpan Timeout
    {
        get => _timeout;
        set
        {
            var minValue = TimeSpan.FromSeconds(10);
            var maxValue = TimeSpan.FromMinutes(30);

            if (value < minValue || value > maxValue)
            {
                throw new ArgumentException(
                    $"Value must be between {minValue.TotalSeconds}s and {maxValue.TotalMinutes}min (current: {value.TotalSeconds}s)",
                    nameof(Timeout));
            }
            _timeout = value;
        }
    }

    /// <summary>
    /// Gets or sets the size of the buffer used for file downloads (4KB-1MB).
    /// </summary>
    public int BufferSize
    {
        get => _bufferSize;
        set
        {
            const int minValue = 4 * 1024;
            const int maxValue = 1024 * 1024;

            if (value < minValue || value > maxValue)
            {
                throw new ArgumentException(
                    $"Value must be between {minValue / 1024}KB and {maxValue / 1024}KB (current: {value / 1024}KB)",
                    nameof(BufferSize));
            }
            _bufferSize = value;
        }
    }

    /// <summary>
    /// Gets or sets the maximum number of retry attempts for failed requests (0-5).
    /// </summary>
    public int MaxRetries
    {
        get => _maxRetries;
        set
        {
            const int minValue = 0;
            const int maxValue = 5;

            if (value < minValue || value > maxValue)
            {
                throw new ArgumentException(
                    $"Value must be between {minValue} and {maxValue} (current: {value})",
                    nameof(MaxRetries));
            }
            _maxRetries = value;
        }
    }

    /// <summary>
    /// Gets or sets the delay in milliseconds between retry attempts (100ms-10s).
    /// The actual delay will be this value multiplied by the attempt number for exponential backoff.
    /// </summary>
    public int RetryDelayMilliseconds
    {
        get => _retryDelayMilliseconds;
        set
        {
            const int minValue = 100;
            const int maxValue = 10000;

            if (value < minValue || value > maxValue)
            {
                throw new ArgumentException(
                    $"Value must be between {minValue}ms and {maxValue}ms (current: {value}ms)",
                    nameof(RetryDelayMilliseconds));
            }
            _retryDelayMilliseconds = value;
        }
    }

    /// <summary>
    /// Validates all option values and throws an exception if any values are invalid.
    /// </summary>
    public void Validate()
    {
        _ = MaxConcurrentDownloads;
        _ = ProgressUpdateInterval;
        _ = Timeout;
        _ = BufferSize;
        _ = MaxRetries;
        _ = RetryDelayMilliseconds;

        if (Token != null && string.IsNullOrWhiteSpace(Token))
        {
            throw new ArgumentException(
                "Token if provided must not be empty or whitespace",
                nameof(Token));
        }
    }

    /// <summary>
    /// Resets all options to their default values.
    /// </summary>
    public void Reset()
    {
        Token = null;
        MaxConcurrentDownloads = DefaultMaxConcurrentDownloads;
        ProgressUpdateInterval = DefaultProgressUpdateInterval;
        Timeout = DefaultTimeout;
        BufferSize = DefaultBufferSize;
        MaxRetries = DefaultMaxRetries;
        RetryDelayMilliseconds = DefaultRetryDelayMilliseconds;
    }

    public override string ToString() =>
        $"""
        HuggingFaceClientOptions:
          MaxConcurrentDownloads: {MaxConcurrentDownloads}
          ProgressUpdateInterval: {ProgressUpdateInterval}ms
          Timeout: {Timeout.TotalSeconds}s
          BufferSize: {BufferSize / 1024}KB
          MaxRetries: {MaxRetries}
          RetryDelay: {RetryDelayMilliseconds}ms
          Token: {(Token == null ? "Not Set" : "Set")}
        """;
}