using LMSupplyDepots.External.HuggingFace.Common;
using LMSupplyDepots.External.HuggingFace.Download;
using LMSupplyDepots.External.HuggingFace.Models;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LMSupplyDepots.External.HuggingFace.Client;

/// <summary>
/// Client for interacting with the Hugging Face API.
/// </summary>
public class HuggingFaceClient : IHuggingFaceClient, IRepositoryDownloader, IDisposable
{
    private readonly HuggingFaceClientOptions _options;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly ILogger<HuggingFaceClient>? _logger;
    private readonly HttpClient _httpClient;
    private readonly FileDownloadManager _downloadManager;
    private bool _disposed;

    public HuggingFaceClient(
        HuggingFaceClientOptions options,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _options = options;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory?.CreateLogger<HuggingFaceClient>();
        _httpClient = CreateHttpClient(_options);

        var downloadManagerLogger = loggerFactory?.CreateLogger<FileDownloadManager>();
        _downloadManager = new FileDownloadManager(_httpClient, downloadManagerLogger, _options.BufferSize);
    }

    public HuggingFaceClient(
        HuggingFaceClientOptions options,
        HttpMessageHandler handler,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _options = options;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory?.CreateLogger<HuggingFaceClient>();
        _httpClient = new HttpClient(handler) { Timeout = options.Timeout };

        if (!string.IsNullOrWhiteSpace(options.Token))
        {
            _httpClient.DefaultRequestHeaders.Add(
                HuggingFaceConstants.Headers.Authorization,
                string.Format(HuggingFaceConstants.Headers.AuthorizationFormat, options.Token));
        }

        var downloadManagerLogger = loggerFactory?.CreateLogger<FileDownloadManager>();
        _downloadManager = new FileDownloadManager(_httpClient, downloadManagerLogger, _options.BufferSize);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<HuggingFaceModel>> SearchTextGenerationModelsAsync(
        string? search = null,
        string[]? filters = null,
        int limit = 5,
        ModelSortField sortField = ModelSortField.Downloads,
        bool descending = true,
        CancellationToken cancellationToken = default)
    {
        return SearchModelsInternalAsync(
            search,
            ModelFilters.TextGenerationFilters,
            filters,
            limit,
            sortField,
            descending,
            cancellationToken);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<HuggingFaceModel>> SearchEmbeddingModelsAsync(
        string? search = null,
        string[]? filters = null,
        int limit = 5,
        ModelSortField sortField = ModelSortField.Downloads,
        bool descending = true,
        CancellationToken cancellationToken = default)
    {
        return SearchModelsInternalAsync(
            search,
            ModelFilters.EmbeddingFilters,
            filters,
            limit,
            sortField,
            descending,
            cancellationToken);
    }

    private async Task<IReadOnlyList<HuggingFaceModel>> SearchModelsInternalAsync(
        string? search,
        IEnumerable<string> requiredFilters,
        string[]? additionalFilters,
        int limit,
        ModelSortField sortField,
        bool descending,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        try
        {
            var allFilters = (additionalFilters ?? [])
                .Concat(requiredFilters)
                .ToArray();

            var requestUri = HuggingFaceConstants.UrlBuilder.CreateModelSearchUrl(
                search,
                allFilters,
                limit,
                sortField.ToApiString(),
                descending);

            _logger?.LogInformation(
                "Searching models with URL: {RequestUri}\nParameters: search={Search}, filters={Filters}, limit={Limit}",
                requestUri, search, string.Join(", ", allFilters), limit);

            return await RetryHandler.ExecuteWithRetryAsync(
                async () =>
                {
                    var response = await _httpClient.GetAsync(requestUri, cancellationToken);
                    var content = await response.Content.ReadAsStringAsync(cancellationToken);

                    if (!response.IsSuccessStatusCode)
                    {
                        _logger?.LogError(
                            "API request failed: {StatusCode}\nResponse: {Content}",
                            response.StatusCode, content);

                        throw new HuggingFaceException(
                            $"API request failed with status code {response.StatusCode}",
                            response.StatusCode);
                    }

                    _logger?.LogDebug("API Response: {Content}", content);

                    var models = await response.Content.ReadFromJsonAsync<HuggingFaceModel[]>(
                        cancellationToken: cancellationToken) ?? [];

                    _logger?.LogInformation("Found {Count} models matching criteria", models.Length);

                    return models;
                },
                _options.MaxRetries,
                _options.RetryDelayMilliseconds,
                _logger,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error searching models");
            throw new HuggingFaceException(
                "Failed to search models",
                (ex as HttpRequestException)?.StatusCode ?? HttpStatusCode.InternalServerError,
                ex);
        }
    }

    /// <summary>
    /// Asynchronously finds a model by its repository ID.
    /// </summary>
    public async Task<HuggingFaceModel> FindModelByRepoIdAsync(
        string repoId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(repoId);

        var requestUri = HuggingFaceConstants.UrlBuilder.CreateModelUrl(repoId);

        try
        {
            _logger?.LogInformation("Finding model by repository ID: {RepoId}", repoId);

            return await RetryHandler.ExecuteWithRetryAsync(
                async () =>
                {
                    using var response = await _httpClient.GetAsync(requestUri, cancellationToken);

                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        throw new HuggingFaceException(
                            $"Model '{repoId}' requires authentication. Please provide a valid API token with the necessary permissions.",
                            HttpStatusCode.Unauthorized);
                    }

                    response.EnsureSuccessStatusCode();

                    var json = await response.Content.ReadAsStringAsync(cancellationToken);

                    _logger?.LogDebug("Model API response for {RepoId}: {ResponseLength} bytes",
                        repoId, json.Length);

                    // Log the first 1000 characters for debugging if we're at trace level
                    if (_logger?.IsEnabled(LogLevel.Trace) == true)
                    {
                        _logger.LogTrace("Model API response preview: {Preview}",
                            json.Length > 1000 ? json.Substring(0, 1000) + "..." : json);
                    }


                    var model = JsonSerializer.Deserialize<HuggingFaceModel>(json);

                    return model ?? throw new HuggingFaceException(
                            $"Model with repository ID '{repoId}' was not found.",
                            HttpStatusCode.NotFound);
                },
                _options.MaxRetries,
                _options.RetryDelayMilliseconds,
                _logger,
                cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new HuggingFaceException(
                $"Model with repository ID '{repoId}' does not exist.",
                HttpStatusCode.NotFound, ex);
        }
        catch (HuggingFaceException)
        {
            // Re-throw HuggingFaceException without wrapping to preserve the original message
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error finding model by repository ID: {RepoId}", repoId);

            // Check if this is an authentication issue
            if (ex is HttpRequestException httpEx && httpEx.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new HuggingFaceException(
                    $"Access to model '{repoId}' requires authentication. Please provide a valid API token.",
                    HttpStatusCode.Unauthorized, ex);
            }

            throw new HuggingFaceException(
                $"Failed to find model with repository ID '{repoId}'",
                (ex as HttpRequestException)?.StatusCode ?? HttpStatusCode.InternalServerError,
                ex);
        }
    }

    /// <inheritdoc/>
    public async Task<HuggingFaceFile> GetFileInfoAsync(
        string repoId,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(repoId);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var requestUri = HuggingFaceConstants.UrlBuilder.CreateFileUrl(repoId, filePath);

        try
        {
            _logger?.LogInformation("Getting file info: {RepoId}/{FilePath}", repoId, filePath);

            return await RetryHandler.ExecuteWithRetryAsync(
                async () =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Head, requestUri);
                    using var response = await _httpClient.SendAsync(request,
                        HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                    response.EnsureSuccessStatusCode();

                    var fileInfo = new HuggingFaceFile
                    {
                        Name = Path.GetFileName(filePath),
                        Path = filePath,
                        Size = response.Content.Headers.ContentLength,
                        MimeType = response.Content.Headers.ContentType?.MediaType,
                        LastModified = response.Content.Headers.LastModified?.UtcDateTime
                    };

                    if (IsTextMimeType(fileInfo.MimeType))
                    {
                        using var getResponse = await _httpClient.GetAsync(requestUri, cancellationToken);
                        getResponse.EnsureSuccessStatusCode();
                        fileInfo.Content = await getResponse.Content.ReadAsStringAsync(cancellationToken);
                    }

                    return fileInfo;
                },
                _options.MaxRetries,
                _options.RetryDelayMilliseconds,
                _logger,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error getting file info: {RepoId}/{FilePath}", repoId, filePath);
            throw new HuggingFaceException($"Failed to get file info for '{repoId}/{filePath}'",
                (ex as HttpRequestException)?.StatusCode ?? HttpStatusCode.InternalServerError, ex);
        }
    }

    /// <summary>
    /// Gets file information from a repository or directory.
    /// </summary>
    public async Task<IReadOnlyList<RepositoryFileInfo>> GetRepositoryFilesAsync(
        string repoId,
        string? treePath = null,
        CancellationToken cancellationToken = default)
    {
        var path = treePath != null
            ? $"{repoId}/tree/main/{treePath}"
            : $"{repoId}/tree/main";

        // Do not use Uri.EscapeDataString as it breaks the path structure
        var requestUri = $"https://huggingface.co/api/models/{path}";

        try
        {
            _logger?.LogDebug("Getting repository files from URL: {Url}", requestUri);

            return await RetryHandler.ExecuteWithRetryAsync(
                async () =>
                {
                    using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
                    response.EnsureSuccessStatusCode();

                    var json = await response.Content.ReadAsStringAsync(cancellationToken);

                    _logger?.LogDebug("Repository files API response: {ResponseLength} bytes", json.Length);

                    // Check response type using JsonDocument
                    using var document = JsonDocument.Parse(json);
                    var root = document.RootElement;

                    // If the response is an array
                    if (root.ValueKind == JsonValueKind.Array)
                    {
                        var options = new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        };

                        var files = JsonSerializer.Deserialize<List<RepositoryFileInfo>>(json, options);
                        return files ?? new List<RepositoryFileInfo>();
                    }
                    // If the response is a single object
                    else if (root.ValueKind == JsonValueKind.Object)
                    {
                        var options = new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        };

                        // Try to deserialize as a single RepositoryFileInfo
                        var file = JsonSerializer.Deserialize<RepositoryFileInfo>(json, options);

                        return file != null
                            ? new List<RepositoryFileInfo> { file }
                            : new List<RepositoryFileInfo>();
                    }

                    // Unknown format, return empty list
                    _logger?.LogWarning("Unexpected response format from API: {Format}", root.ValueKind);
                    return new List<RepositoryFileInfo>();
                },
                _options.MaxRetries,
                _options.RetryDelayMilliseconds,
                _logger,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to get repository files for '{Path}'", path);
            throw new HuggingFaceException(
                $"Failed to get repository files for '{path}'",
                (ex as HttpRequestException)?.StatusCode ?? HttpStatusCode.InternalServerError,
                ex);
        }
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<RepoDownloadProgress> DownloadRepositoryAsync(
        string repoId,
        string outputDir,
        bool useSubDir = true,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var repoManagerLogger = _loggerFactory?.CreateLogger<RepositoryDownloadManager>();
        var downloader = new RepositoryDownloadManager(
            this,
            repoManagerLogger,
            _options.MaxConcurrentDownloads,
            _options.ProgressUpdateInterval);

        return downloader.DownloadRepositoryAsync(repoId, outputDir, useSubDir, cancellationToken);
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<RepoDownloadProgress> DownloadRepositoryFilesAsync(
        string repoId,
        IEnumerable<string> filePaths,
        string outputDir,
        bool useSubDir = true,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var repoManagerLogger = _loggerFactory?.CreateLogger<RepositoryDownloadManager>();
        var downloader = new RepositoryDownloadManager(
            this,
            repoManagerLogger,
            _options.MaxConcurrentDownloads,
            _options.ProgressUpdateInterval);

        return downloader.DownloadRepositoryFilesAsync(repoId, filePaths, outputDir, useSubDir, cancellationToken);
    }

    /// <summary>
    /// Gets file sizes for all files in a repository including subdirectories.
    /// </summary>
    public async Task<Dictionary<string, long>> GetRepositoryFileSizesAsync(
        string repoId,
        CancellationToken cancellationToken = default)
    {
        var requestUri = $"https://huggingface.co/api/models/{repoId}";

        try
        {
            var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            var model = await FindModelByRepoIdAsync(repoId, cancellationToken);

            // siblings 배열에서 각 파일의 경로 추출
            var siblings = model.Siblings ?? [];
            // 하위경로에 GGUF 파일이 있는지 확인
            var subFiles = siblings.Where(p => p.Filename.Contains('/') && p.Filename.EndsWith(".gguf"));
            if (subFiles.Any())
            {
                var dirGroups = siblings
                    .Where(s => !string.IsNullOrEmpty(s.Filename))
                    .GroupBy(s => Path.GetDirectoryName(s.Filename))
                    .Where(g => !string.IsNullOrEmpty(g.Key));

                // 각 디렉토리별로 파일 크기 조회
                foreach (var group in dirGroups)
                {
                    var treePath = group.Key!.Replace('\\', '/');
                    await GetFileSizesInDirectoryAsync(repoId, treePath, result, cancellationToken);
                }
            }
            else
            {
                // 루트 디렉토리의 파일들 크기 조회
                var rootFiles = siblings
                        .Where(s => !string.IsNullOrEmpty(s.Filename) &&
                                   !s.Filename.Contains('/') &&
                                   !s.Filename.Contains('\\'));
                if (rootFiles.Any())
                {
                    await GetFileSizesInDirectoryAsync(repoId, "", result, cancellationToken);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to get repository file sizes for {RepoId}", repoId);
            throw new HuggingFaceException(
                $"Failed to get repository file sizes for '{repoId}'",
                (ex as HttpRequestException)?.StatusCode ?? HttpStatusCode.InternalServerError,
                ex);
        }
    }

    private async Task GetFileSizesInDirectoryAsync(
            string repoId,
            string treePath,
            Dictionary<string, long> results,
            CancellationToken cancellationToken)
    {
        var path = !string.IsNullOrEmpty(treePath)
            ? $"{repoId}/tree/main/{treePath}"
            : $"{repoId}/tree/main";

        // Do not use Uri.EscapeDataString as it breaks the path structure
        var requestUri = $"https://huggingface.co/api/models/{path}";

        try
        {
            _logger?.LogDebug("Getting file sizes from URL: {Url}", requestUri);

            var files = await GetRepositoryFilesAsync(repoId, treePath, cancellationToken);

            foreach (var file in files)
            {
                if (file.IsFile)
                {
                    results[file.Path] = file.GetEffectiveSize();
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex,
                "Failed to get repository tree for '{RepoId}' at path '{TreePath}'",
                repoId, treePath);
        }
    }

    private async Task GetRepositoryFileSizesInternalAsync(
            string repoId,
            string treePath,
            Dictionary<string, long> results,
            CancellationToken cancellationToken)
    {
        var path = !string.IsNullOrEmpty(treePath)
            ? $"{repoId}/tree/main/{treePath}"
            : $"{repoId}/tree/main";

        // Do not use Uri.EscapeDataString as it breaks the path structure
        var requestUri = $"https://huggingface.co/api/models/{path}";

        try
        {
            _logger?.LogDebug("Getting repository file sizes from URL: {Url}", requestUri);

            var files = await GetRepositoryFilesAsync(repoId, treePath, cancellationToken);

            foreach (var file in files)
            {
                if (file.IsDirectory) // directory type is "tree" or "directory" in the API
                {
                    var dirName = file.Path.Split('/').Last();
                    var subPath = string.IsNullOrEmpty(treePath)
                        ? dirName
                        : $"{treePath}/{dirName}";

                    await GetRepositoryFileSizesInternalAsync(
                        repoId, subPath, results, cancellationToken);
                }
                else if (file.IsFile) // file type is "blob" or "file" in the API
                {
                    results[file.Path] = file.GetEffectiveSize();
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex,
                "Failed to get repository tree for '{RepoId}' at path '{TreePath}'",
                repoId, treePath);
            throw new HuggingFaceException(
                $"Failed to get repository tree for '{repoId}' at path '{treePath}'",
                (ex as HttpRequestException)?.StatusCode ?? HttpStatusCode.InternalServerError,
                ex);
        }
    }

    public async Task<FileDownloadResult> DownloadFileWithResultAsync(
        string repoId,
        string filePath,
        string outputPath,
        long startFrom = 0,
        IProgress<FileDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(repoId);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var requestUri = HuggingFaceConstants.UrlBuilder.CreateFileUrl(repoId, filePath);
        return await _downloadManager.DownloadWithResultAsync(
            requestUri,
            outputPath,
            startFrom,
            progress,
            cancellationToken);
    }

    /// <inheritdoc/>
    public string? GetDownloadUrl(string repoId, string filePath)
    {
        if (string.IsNullOrWhiteSpace(repoId) || string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        try
        {
            return HuggingFaceConstants.UrlBuilder.CreateFileUrl(repoId, filePath);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error creating download URL for {RepoId}/{FilePath}", repoId, filePath);
            return null;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _httpClient.Dispose();
            _disposed = true;
        }

        GC.SuppressFinalize(this);
    }

    private static HttpClient CreateHttpClient(HuggingFaceClientOptions options)
    {
        var client = new HttpClient
        {
            Timeout = options.Timeout
        };

        if (!string.IsNullOrWhiteSpace(options.Token))
        {
            client.DefaultRequestHeaders.Add(
                HuggingFaceConstants.Headers.Authorization,
                string.Format(HuggingFaceConstants.Headers.AuthorizationFormat, options.Token));
        }

        return client;
    }

    private static bool IsTextMimeType(string? mimeType)
    {
        if (string.IsNullOrEmpty(mimeType))
            return false;

        return mimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
               mimeType.Equals("application/json", StringComparison.OrdinalIgnoreCase) ||
               mimeType.Equals("application/xml", StringComparison.OrdinalIgnoreCase) ||
               mimeType.Equals("application/javascript", StringComparison.OrdinalIgnoreCase);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(HuggingFaceClient));
    }
}