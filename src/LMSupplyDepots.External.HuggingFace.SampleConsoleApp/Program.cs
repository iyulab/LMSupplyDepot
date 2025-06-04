using LMSupplyDepots.External.HuggingFace.Client;
using LMSupplyDepots.External.HuggingFace.Common;
using LMSupplyDepots.External.HuggingFace.Download;
using LMSupplyDepots.External.HuggingFace.Models;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using System.Text.Json;

namespace LMSupplyDepots.External.HuggingFace.SampleConsoleApp;

class Program
{
    static async Task Main(string[] args)
    {
        // Setup Serilog logger
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("System", LogEventLevel.Information)
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        // Create logger factory for HuggingFaceClient
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddSerilog(Log.Logger);
        });

        try
        {
            Console.WriteLine("=== LMSupplyDepots.External.HuggingFace Sample Application ===\n");

            // Initialize client with options
            var options = new HuggingFaceClientOptions
            {
                // Uncomment and provide your token if needed
                // Token = "your-huggingface-token",
                MaxConcurrentDownloads = 3,
                ProgressUpdateInterval = 200,
                Timeout = TimeSpan.FromMinutes(10),
                BufferSize = 8 * 1024, // 8KB
                MaxRetries = 3,
                RetryDelayMilliseconds = 1000
            };

            // Display options
            Console.WriteLine("Client Configuration:");
            Console.WriteLine(options.ToString());
            Console.WriteLine();

            // Create client
            using var client = new HuggingFaceClient(options, loggerFactory);

            // Display menu
            while (true)
            {
                Console.WriteLine("\nSelect an operation:");
                Console.WriteLine("1. Search for Text Generation Models");
                Console.WriteLine("2. Search for Embedding Models");
                Console.WriteLine("3. Find Model by Repository ID");
                Console.WriteLine("4. Get Repository File Sizes");
                Console.WriteLine("5. Download a Single File");
                Console.WriteLine("6. Download Repository Files");
                Console.WriteLine("7. Analyze Model Artifacts");
                Console.WriteLine("0. Exit");
                Console.Write("\nYour choice: ");

                if (!int.TryParse(Console.ReadLine(), out int choice))
                {
                    Console.WriteLine("Invalid input. Please enter a number.");
                    continue;
                }

                switch (choice)
                {
                    case 1:
                        await SearchTextGenerationModelsAsync(client);
                        break;
                    case 2:
                        await SearchEmbeddingModelsAsync(client);
                        break;
                    case 3:
                        await FindModelByRepoIdAsync(client);
                        break;
                    case 4:
                        await GetRepositoryFileSizesAsync(client);
                        break;
                    case 5:
                        await DownloadSingleFileAsync(client);
                        break;
                    case 6:
                        await DownloadRepositoryFilesAsync(client);
                        break;
                    case 7:
                        await AnalyzeModelArtifactsAsync(client);
                        break;
                    case 0:
                        Console.WriteLine("Exiting application...");
                        return;
                    default:
                        Console.WriteLine("Invalid choice. Please try again.");
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "An error occurred in the application");
            Console.WriteLine($"Error: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Inner Error: {ex.InnerException.Message}");
            }
        }
        finally
        {
            // Ensure proper cleanup of Serilog
            Log.CloseAndFlush();
        }
    }

    private static async Task SearchTextGenerationModelsAsync(IHuggingFaceClient client)
    {
        Console.Write("Enter search term (or leave empty): ");
        var searchTerm = Console.ReadLine();

        Console.Write("Enter limit (default 5): ");
        if (!int.TryParse(Console.ReadLine(), out int limit) || limit <= 0)
        {
            limit = 5;
        }

        Console.WriteLine("\nSearching for text generation models...");
        var models = await client.SearchTextGenerationModelsAsync(
            search: string.IsNullOrWhiteSpace(searchTerm) ? null : searchTerm,
            limit: limit,
            sortField: ModelSortField.Downloads,
            descending: true);

        Console.WriteLine($"\nFound {models.Count} models:");
        foreach (var model in models)
        {
            Console.WriteLine($"\nModel: {model.ModelId}");
            Console.WriteLine($"Author: {model.Author}");
            Console.WriteLine($"Downloads: {model.Downloads:N0}");
            Console.WriteLine($"Last Modified: {model.LastModified.ToLocalTime():yyyy-MM-dd HH:mm:ss}");

            var tags = model.Tags.Count > 0 ? string.Join(", ", model.Tags) : "None";
            Console.WriteLine($"Tags: {tags}");

            var ggufFiles = model.GetGgufModelPaths();
            Console.WriteLine($"GGUF Files: {ggufFiles.Length}");

            if (ggufFiles.Length > 0)
            {
                Console.WriteLine("Files:");
                foreach (var file in ggufFiles.Take(3))
                {
                    Console.WriteLine($" - {file}");
                }
                if (ggufFiles.Length > 3)
                {
                    Console.WriteLine($" - ... and {ggufFiles.Length - 3} more");
                }
            }
        }
    }

    private static async Task SearchEmbeddingModelsAsync(IHuggingFaceClient client)
    {
        Console.Write("Enter search term (or leave empty): ");
        var searchTerm = Console.ReadLine();

        Console.Write("Enter limit (default 5): ");
        if (!int.TryParse(Console.ReadLine(), out int limit) || limit <= 0)
        {
            limit = 5;
        }

        Console.WriteLine("\nSearching for embedding models...");
        var models = await client.SearchEmbeddingModelsAsync(
            search: string.IsNullOrWhiteSpace(searchTerm) ? null : searchTerm,
            limit: limit,
            sortField: ModelSortField.Downloads,
            descending: true);

        Console.WriteLine($"\nFound {models.Count} models:");
        foreach (var model in models)
        {
            Console.WriteLine($"\nModel: {model.ModelId}");
            Console.WriteLine($"Author: {model.Author}");
            Console.WriteLine($"Downloads: {model.Downloads:N0}");
            Console.WriteLine($"Last Modified: {model.LastModified.ToLocalTime():yyyy-MM-dd HH:mm:ss}");

            var tags = model.Tags.Count > 0 ? string.Join(", ", model.Tags) : "None";
            Console.WriteLine($"Tags: {tags}");
        }
    }

    private static async Task FindModelByRepoIdAsync(IHuggingFaceClient client)
    {
        Console.Write("Enter repository ID (e.g., thebloke/Llama-2-7B-GGUF): ");
        var repoId = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(repoId))
        {
            Console.WriteLine("Repository ID cannot be empty.");
            return;
        }

        try
        {
            Console.WriteLine($"\nFinding model details for {repoId}...");
            var model = await client.FindModelByRepoIdAsync(repoId);

            Console.WriteLine($"\nModel: {model.ModelId}");
            Console.WriteLine($"Author: {model.Author}");
            Console.WriteLine($"Downloads: {model.Downloads:N0}");
            Console.WriteLine($"Last Modified: {model.LastModified.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"Created At: {model.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"Private: {model.IsPrivate}");
            Console.WriteLine($"Library: {model.LibraryName}");

            var tags = model.Tags.Count > 0 ? string.Join(", ", model.Tags) : "None";
            Console.WriteLine($"Tags: {tags}");

            if (model.Siblings != null)
            {
                Console.WriteLine($"Files: {model.Siblings.Count}");
                if (model.Siblings.Count > 0)
                {
                    Console.WriteLine("\nFile list (up to 10):");
                    foreach (var file in model.Siblings.Take(10))
                    {
                        Console.WriteLine($" - {file.Filename}");
                    }
                    if (model.Siblings.Count > 10)
                    {
                        Console.WriteLine($" - ... and {model.Siblings.Count - 10} more");
                    }
                }
            }

            // Check for additional properties
            Console.WriteLine("\nAdditional Properties:");
            foreach (var prop in model.GetAvailableProperties().Take(5))
            {
                var value = model.GetRawProperty(prop);
                Console.WriteLine($" - {prop}: {(value?.ToString() ?? "null")}");
            }
        }
        catch (HuggingFaceException ex)
        {
            Console.WriteLine($"Error finding model: {ex.Message}");
            if (ex.StatusCode.HasValue)
            {
                Console.WriteLine($"Status code: {ex.StatusCode}");
            }
        }
    }

    private static async Task GetRepositoryFileSizesAsync(IHuggingFaceClient client)
    {
        Console.Write("Enter repository ID: ");
        var repoId = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(repoId))
        {
            Console.WriteLine("Repository ID cannot be empty.");
            return;
        }

        try
        {
            Console.WriteLine($"\nGetting file sizes for {repoId}...");
            var fileSizes = await client.GetRepositoryFileSizesAsync(repoId);

            Console.WriteLine($"\nFound {fileSizes.Count} files:");
            long totalSize = 0;

            foreach (var file in fileSizes.OrderByDescending(f => f.Value))
            {
                totalSize += file.Value;
                Console.WriteLine($" - {file.Key}: {StringFormatter.FormatSize(file.Value, 2)}");
            }

            Console.WriteLine($"\nTotal Size: {StringFormatter.FormatSize(totalSize, 2)}");
        }
        catch (HuggingFaceException ex)
        {
            Console.WriteLine($"Error getting file sizes: {ex.Message}");
        }
    }

    private static async Task DownloadSingleFileAsync(IHuggingFaceClient client)
    {
        Console.Write("Enter repository ID: ");
        var repoId = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(repoId))
        {
            Console.WriteLine("Repository ID cannot be empty.");
            return;
        }

        Console.Write("Enter file path within the repository: ");
        var filePath = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(filePath))
        {
            Console.WriteLine("File path cannot be empty.");
            return;
        }

        Console.Write("Enter local output path: ");
        var outputPath = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            // Use default output path in current directory
            outputPath = Path.Combine(Environment.CurrentDirectory, Path.GetFileName(filePath));
            Console.WriteLine($"Using default output path: {outputPath}");
        }

        try
        {
            Console.WriteLine($"\nDownloading {filePath} from {repoId} to {outputPath}...");

            // Create a progress tracker
            var lastProgressUpdate = DateTime.MinValue;
            var progress = new Progress<FileDownloadProgress>(p =>
            {
                // Limit updates to once per second to avoid console spam
                if ((DateTime.Now - lastProgressUpdate).TotalSeconds >= 1)
                {
                    lastProgressUpdate = DateTime.Now;

                    Console.Write($"\rProgress: {p.FormattedProgress} | " +
                                 $"Size: {p.FormattedCurrentSize}/{p.FormattedTotalSize} | " +
                                 $"Speed: {p.FormattedDownloadSpeed} | " +
                                 $"Remaining: {p.FormattedRemainingTime}");
                }
            });

            // Start download
            var result = await client.DownloadFileWithResultAsync(
                repoId,
                filePath,
                outputPath,
                progress: progress);

            Console.WriteLine("\n\nDownload completed!");
            Console.WriteLine($"Downloaded: {StringFormatter.FormatSize(result.BytesDownloaded, 2)}");
            Console.WriteLine($"Output Path: {outputPath}");
        }
        catch (HuggingFaceException ex)
        {
            Console.WriteLine($"\nError downloading file: {ex.Message}");
        }
    }

    private static async Task DownloadRepositoryFilesAsync(IHuggingFaceClient client)
    {
        Console.Write("Enter repository ID: ");
        var repoId = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(repoId))
        {
            Console.WriteLine("Repository ID cannot be empty.");
            return;
        }

        Console.Write("Enter specific files to download (comma-separated) or leave empty for all files: ");
        var fileInput = Console.ReadLine();
        string[]? filesToDownload = null;

        if (!string.IsNullOrWhiteSpace(fileInput))
        {
            filesToDownload = fileInput.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Console.WriteLine($"Will download {filesToDownload.Length} specified files.");
        }
        else
        {
            Console.WriteLine("Will download all repository files.");
        }

        Console.Write("Enter local output directory: ");
        var outputDir = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(outputDir))
        {
            // Use default output directory
            outputDir = Path.Combine(Environment.CurrentDirectory, repoId.Replace('/', '_'));
            Console.WriteLine($"Using default output directory: {outputDir}");
        }

        try
        {
            Console.WriteLine($"\nDownloading from {repoId} to {outputDir}...");

            // Progress variables for display
            var lastProgressUpdate = DateTime.MinValue;
            var currentFiles = new Dictionary<string, string>();

            // Start download
            IAsyncEnumerable<RepoDownloadProgress> progressStream;

            if (filesToDownload != null)
            {
                progressStream = client.DownloadRepositoryFilesAsync(
                    repoId, filesToDownload, outputDir);
            }
            else
            {
                progressStream = client.DownloadRepositoryAsync(
                    repoId, outputDir);
            }

            await foreach (var progress in progressStream)
            {
                // Limit updates to once per second
                if ((DateTime.Now - lastProgressUpdate).TotalSeconds >= 1)
                {
                    lastProgressUpdate = DateTime.Now;

                    Console.Clear();
                    Console.WriteLine($"Download Progress: {StringFormatter.FormatProgress(progress.TotalProgress)}");
                    Console.WriteLine($"Completed: {progress.CompletedFiles.Count}/{progress.TotalFiles.Count} files");

                    // Show current file downloads
                    if (progress.CurrentProgresses.Count > 0)
                    {
                        Console.WriteLine("\nCurrent Downloads:");
                        foreach (var fileProgress in progress.CurrentProgresses)
                        {
                            var fileName = Path.GetFileName(fileProgress.UploadPath);
                            Console.WriteLine($" - {fileName} | " +
                                             $"{fileProgress.FormattedProgress} | " +
                                             $"{fileProgress.FormattedCurrentSize}/{fileProgress.FormattedTotalSize} | " +
                                             $"{fileProgress.FormattedDownloadSpeed}");
                        }
                    }

                    // Show recently completed files
                    if (progress.CompletedFiles.Count > 0)
                    {
                        Console.WriteLine("\nRecently Completed Files:");
                        foreach (var file in progress.CompletedFiles.Take(5))
                        {
                            Console.WriteLine($" - {Path.GetFileName(file)}");
                        }
                        if (progress.CompletedFiles.Count > 5)
                        {
                            Console.WriteLine($" - ... and {progress.CompletedFiles.Count - 5} more");
                        }
                    }
                }

                if (progress.IsCompleted)
                {
                    Console.WriteLine("\nDownload completed!");
                    Console.WriteLine($"All {progress.CompletedFiles.Count} files have been downloaded to {outputDir}");
                }
            }

            Console.WriteLine("\nRepository download process finished.");
        }
        catch (HuggingFaceException ex)
        {
            Console.WriteLine($"\nError downloading repository: {ex.Message}");
        }
    }

    private static async Task AnalyzeModelArtifactsAsync(IHuggingFaceClient client)
    {
        Console.Write("Enter repository ID: ");
        var repoId = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(repoId))
        {
            Console.WriteLine("Repository ID cannot be empty.");
            return;
        }

        try
        {
            Console.WriteLine($"\nAnalyzing model artifacts for {repoId}...");

            // Implement extension method call if available in your version
            // Get model and file sizes
            var model = await client.FindModelByRepoIdAsync(repoId);
            var fileSizes = await client.GetRepositoryFileSizesAsync(repoId);

            // Use the analyzer
            var artifacts = LMModelArtifactAnalyzer.GetModelArtifacts(model, fileSizes);

            Console.WriteLine($"\nFound {artifacts.Count} model artifacts:");

            foreach (var artifact in artifacts)
            {
                Console.WriteLine($"\nArtifact Name: {artifact.Name}");
                Console.WriteLine($"Format: {artifact.Format}");
                Console.WriteLine($"Total Size: {StringFormatter.FormatSize(artifact.TotalSize, 2)}");
                Console.WriteLine($"File Count: {artifact.Files.Count}");

                Console.WriteLine("Files:");
                foreach (var file in artifact.Files)
                {
                    Console.WriteLine($" - {Path.GetFileName(file.Path)} ({StringFormatter.FormatSize(file.Size, 2)})");
                }
            }
        }
        catch (HuggingFaceException ex)
        {
            Console.WriteLine($"\nError analyzing model artifacts: {ex.Message}");
        }
    }
}