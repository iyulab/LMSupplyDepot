using System.Net;

namespace LMSupplyDepots.External.HuggingFace.Common;

/// <summary>
/// Provides retry functionality for operations that may fail temporarily.
/// </summary>
internal static class RetryHandler
{
    private static readonly HashSet<HttpStatusCode> RetryableStatusCodes =
    [
        HttpStatusCode.RequestTimeout,       // 408
        HttpStatusCode.InternalServerError,  // 500
        HttpStatusCode.BadGateway,           // 502
        HttpStatusCode.ServiceUnavailable,   // 503
        HttpStatusCode.GatewayTimeout        // 504
    ];

    /// <summary>
    /// Executes an operation with retry logic.
    /// </summary>
    /// <typeparam name="T">The type of the result.</typeparam>
    public static async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> operation,
        int maxRetries,
        int baseDelay,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var attempts = 0;
        while (true)
        {
            try
            {
                attempts++;
                return await operation();
            }
            catch (Exception ex) when (ShouldRetry(ex) && attempts <= maxRetries)
            {
                var delay = CalculateDelay(attempts, baseDelay);
                logger?.LogWarning(ex,
                    "Operation failed on attempt {Attempt}/{MaxRetries}. Retrying in {Delay}ms...",
                    attempts, maxRetries, delay);

                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Executes an operation with retry logic.
    /// </summary>
    public static async Task ExecuteWithRetryAsync(
        Func<Task> operation,
        int maxRetries,
        int baseDelay,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var attempts = 0;
        while (true)
        {
            try
            {
                attempts++;
                await operation();
                return;
            }
            catch (Exception ex) when (ShouldRetry(ex) && attempts <= maxRetries)
            {
                var delay = CalculateDelay(attempts, baseDelay);
                logger?.LogWarning(ex,
                    "Operation failed on attempt {Attempt}/{MaxRetries}. Retrying in {Delay}ms...",
                    attempts, maxRetries, delay);

                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private static bool ShouldRetry(Exception exception)
    {
        return exception switch
        {
            HttpRequestException httpEx => IsRetryableStatusCode(httpEx.StatusCode),
            TimeoutException => true,
            IOException => true,
            _ => false
        };
    }

    private static bool IsRetryableStatusCode(HttpStatusCode? statusCode)
    {
        return statusCode.HasValue && RetryableStatusCodes.Contains(statusCode.Value);
    }

    private static int CalculateDelay(int attempt, int baseDelay)
    {
        // Exponential backoff with jitter
        var exponentialDelay = baseDelay * Math.Pow(2, attempt - 1);
        var maxDelay = Math.Min(exponentialDelay, 30000); // Cap at 30 seconds

        // Add jitter (±20%)
        var random = new Random();
        var jitterPercentage = (random.NextDouble() * 0.4) - 0.2; // -20% to +20%
        var jitterDelay = maxDelay * (1 + jitterPercentage);

        return (int)jitterDelay;
    }
}