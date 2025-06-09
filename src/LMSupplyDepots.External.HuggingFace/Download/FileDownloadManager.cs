using LMSupplyDepots.External.HuggingFace.Client;
using System.Net;

namespace LMSupplyDepots.External.HuggingFace.Download;

/// <summary>
/// Manages file download operations with enhanced cancellation support
/// </summary>
internal sealed class FileDownloadManager(
    HttpClient httpClient,
    ILogger<FileDownloadManager>? logger = null,
    int defaultBufferSize = 8192)
{
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly ILogger<FileDownloadManager>? _logger = logger;
    private readonly int _defaultBufferSize = defaultBufferSize;

    public async Task<FileDownloadResult> DownloadWithResultAsync(
        string url,
        string outputPath,
        long startFrom = 0,
        IProgress<FileDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Starting download from {Url} to {OutputPath}", url, outputPath);

        var request = CreateRequest(url, startFrom);
        using var response = await SendRequestAsync(request, cancellationToken);
        var totalBytes = GetTotalBytes(response, startFrom);

        _logger?.LogInformation("Total file size: {TotalBytes} bytes", totalBytes);

        EnsureDirectory(outputPath);

        var bufferSize = DetermineOptimalBufferSize(totalBytes);
        var progressTracker = new DownloadProgressTracker(startFrom, DateTime.UtcNow);

        // Create a shorter timeout cancellation token for more responsive cancellation
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        using var contentStream = await response.Content.ReadAsStreamAsync(combinedCts.Token);
        using var fileStream = CreateFileStream(outputPath, startFrom, bufferSize);

        var buffer = new byte[bufferSize];
        var lastProgressReport = DateTime.UtcNow;
        var progressReportInterval = TimeSpan.FromMilliseconds(100); // Report progress every 100ms

        try
        {
            while (!combinedCts.Token.IsCancellationRequested)
            {
                // Check cancellation before each read operation
                combinedCts.Token.ThrowIfCancellationRequested();

                // Use a smaller timeout for individual read operations
                using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var readToken = CancellationTokenSource.CreateLinkedTokenSource(
                    combinedCts.Token, readCts.Token);

                int bytesRead;
                try
                {
                    bytesRead = await contentStream.ReadAsync(buffer, readToken.Token);
                }
                catch (OperationCanceledException) when (readCts.Token.IsCancellationRequested && !combinedCts.Token.IsCancellationRequested)
                {
                    // Read timeout occurred but main cancellation was not requested
                    _logger?.LogWarning("Read timeout occurred for {OutputPath}, retrying...", outputPath);
                    continue;
                }

                if (bytesRead == 0) break;

                // Check cancellation before write
                combinedCts.Token.ThrowIfCancellationRequested();

                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), combinedCts.Token);

                // Force flush frequently for better cancellation response
                if (progressTracker.TotalBytesRead % (bufferSize * 4) == 0)
                {
                    await fileStream.FlushAsync(combinedCts.Token);
                }

                var (totalBytesRead, downloadSpeed) = progressTracker.UpdateProgress(bytesRead);

                // Reset timeout on successful read
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

                // Report progress at regular intervals
                var now = DateTime.UtcNow;
                if (now - lastProgressReport >= progressReportInterval || bytesRead == 0)
                {
                    var remainingTime = CalculateRemainingTime(totalBytesRead, totalBytes, downloadSpeed);

                    progress?.Report(FileDownloadProgress.CreateProgress(
                        outputPath,
                        totalBytesRead,
                        totalBytes,
                        downloadSpeed,
                        remainingTime));

                    lastProgressReport = now;
                }

                // Check cancellation after processing
                combinedCts.Token.ThrowIfCancellationRequested();
            }

            // Final flush
            await fileStream.FlushAsync(combinedCts.Token);
        }
        catch (OperationCanceledException)
        {
            _logger?.LogInformation("Download was cancelled: {OutputPath}", outputPath);

            // Try to flush what we have before cancelling
            try
            {
                await fileStream.FlushAsync(CancellationToken.None);
            }
            catch
            {
                // Ignore flush errors during cancellation
            }

            throw;
        }

        _logger?.LogInformation("Download completed: {OutputPath}", outputPath);

        return new FileDownloadResult
        {
            FilePath = outputPath,
            BytesDownloaded = progressTracker.TotalBytesRead,
            TotalBytes = totalBytes,
            IsCompleted = true
        };
    }

    private HttpRequestMessage CreateRequest(string url, long startFrom)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (startFrom > 0)
        {
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(startFrom, null);
            _logger?.LogInformation("Resuming download from byte position {StartFrom}", startFrom);
        }
        return request;
    }

    private async Task<HttpResponseMessage> SendRequestAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new HuggingFaceException(
                    "This model requires authentication. Please provide a valid API token with the necessary permissions.",
                    HttpStatusCode.Unauthorized);
            }

            response.EnsureSuccessStatusCode();
            return response;
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogError(ex, "HTTP request failed: {Url}", request.RequestUri);

            var message = ex.StatusCode == HttpStatusCode.Unauthorized
                ? "This model requires authentication. Please provide a valid API token with the necessary permissions."
                : $"Failed to download file: {request.RequestUri}";

            throw new HuggingFaceException(message, ex.StatusCode ?? HttpStatusCode.InternalServerError, ex);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Request failed: {Url}", request.RequestUri);
            throw new HuggingFaceException(
                $"Failed to download file: {request.RequestUri}",
                HttpStatusCode.InternalServerError,
                ex);
        }
    }

    private static long? GetTotalBytes(HttpResponseMessage response, long startFrom)
    {
        return response.Content.Headers.ContentLength.HasValue
            ? response.Content.Headers.ContentLength.Value + startFrom
            : null;
    }

    private static void EnsureDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory))
        {
            throw new InvalidOperationException($"Invalid directory path: {path}");
        }

        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private int DetermineOptimalBufferSize(long? totalBytes)
    {
        // Use smaller buffer size for more responsive cancellation
        var baseBufferSize = _defaultBufferSize;

        // For very small files, use even smaller buffer
        if (totalBytes.HasValue && totalBytes.Value < baseBufferSize * 4)
        {
            return Math.Max(1024, (int)(totalBytes.Value / 8)); // At least 1KB
        }

        // For larger files, use reasonable size but not too large for cancellation response
        if (!totalBytes.HasValue || totalBytes.Value < baseBufferSize * 8)
            return baseBufferSize;

        var optimalSize = (int)Math.Min(
            Math.Max(
                baseBufferSize,
                Math.Min(totalBytes.Value / 200, 64 * 1024) // 64KB max for better cancellation
            ),
            Environment.SystemPageSize * 8 // Smaller than before
        );

        _logger?.LogDebug("Determined optimal buffer size: {BufferSize} bytes for file size: {FileSize}",
            optimalSize, totalBytes);
        return optimalSize;
    }

    private static FileStream CreateFileStream(string path, long startFrom, int bufferSize)
    {
        return new FileStream(
            path,
            startFrom > 0 ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    private static TimeSpan? CalculateRemainingTime(long bytesDownloaded, long? totalBytes, double downloadSpeed)
    {
        if (!totalBytes.HasValue || downloadSpeed <= 0)
            return null;

        var remainingBytes = totalBytes.Value - bytesDownloaded;
        return TimeSpan.FromSeconds(remainingBytes / downloadSpeed);
    }

    private class DownloadProgressTracker(long initialBytes, DateTime startTime)
    {
        private readonly DateTime _startTime = startTime;
        private readonly List<(DateTime timestamp, long bytes)> _recentChunks = [];
        private const int MaxRecentChunks = 5; // Reduced for faster calculation

        public long TotalBytesRead { get; private set; } = initialBytes;

        public (long TotalBytesRead, double DownloadSpeed) UpdateProgress(int newBytes)
        {
            TotalBytesRead += newBytes;
            var now = DateTime.UtcNow;

            _recentChunks.Add((now, newBytes));
            if (_recentChunks.Count > MaxRecentChunks)
                _recentChunks.RemoveAt(0);

            // Calculate speed from recent chunks only for more responsive updates
            if (_recentChunks.Count >= 2)
            {
                var recentTimeSpan = (now - _recentChunks[0].timestamp).TotalSeconds;
                var recentBytes = _recentChunks.Sum(chunk => chunk.bytes);
                var recentSpeed = recentTimeSpan > 0 ? recentBytes / recentTimeSpan : 0;
                return (TotalBytesRead, recentSpeed);
            }

            // Fallback to overall speed
            var overallTimeSpan = (now - _startTime).TotalSeconds;
            var overallSpeed = overallTimeSpan > 0 ? TotalBytesRead / overallTimeSpan : 0;
            return (TotalBytesRead, overallSpeed);
        }
    }
}