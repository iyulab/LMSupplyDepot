Console.WriteLine("hello");

//using LMSupplyDepots.Models;
//using LMSupplyDepots.ModelHub.Models;
//using LMSupplyDepots.SDK;
//using System.CommandLine;
//using System.CommandLine.NamingConventionBinder;
//using Microsoft.Extensions.Logging;

//namespace LMSupplyDepots.CLI;

//class Program
//{
//    static async Task<int> Main(string[] args)
//    {
//        Console.OutputEncoding = System.Text.Encoding.UTF8;

//        // Setup command line application
//        var rootCommand = new RootCommand("LMSupplyDepots CLI - Manage and run local language models")
//        {
//            // Global options
//            new Option<string>(new[] { "--data-path", "-d" }, "Base directory for storing all data"),
//            new Option<string>(new[] { "--hf-token", "-h" }, "HuggingFace API token"),
//            new Option<LogLevel>(new[] { "--log-level", "-l" }, () => LogLevel.Warning, "Logging level (Critical, Error, Warning, Information, Debug, Trace)")
//        };

//        // List models command
//        var listCommand = new Command("list", "List available models");
//        listCommand.AddOption(new Option<string?>(new[] { "--type", "-t" }, "Filter by model type (text, embedding, multimodal)"));
//        listCommand.AddOption(new Option<string?>(new[] { "--search", "-s" }, "Search term to filter models"));
//        listCommand.Handler = CommandHandler.Create<string?, string?, LogLevel, string?, string?>(ListModelsHandler);
//        rootCommand.AddCommand(listCommand);

//        // Search models command
//        var searchCommand = new Command("search", "Search for models from external sources");
//        searchCommand.AddOption(new Option<string?>(new[] { "--type", "-t" }, "Filter by model type (text, embedding, multimodal)"));
//        searchCommand.AddOption(new Option<string>(new[] { "--query", "-q" }, "Search query") { IsRequired = true });
//        searchCommand.AddOption(new Option<int>(new[] { "--limit", "-n" }, () => 10, "Maximum number of results to return"));
//        searchCommand.Handler = CommandHandler.Create<string?, string?, LogLevel, string?, string?, int>(SearchModelsHandler);
//        rootCommand.AddCommand(searchCommand);

//        // Download model command
//        var downloadCommand = new Command("download", "Download a model from external source");
//        downloadCommand.AddOption(new Option<string>(new[] { "--source", "-s" }, "Source ID of the model to download") { IsRequired = true });
//        downloadCommand.Handler = CommandHandler.Create<string?, string?, LogLevel, string>(DownloadModelHandler);
//        rootCommand.AddCommand(downloadCommand);

//        // Info model command
//        var infoCommand = new Command("info", "Get information about a model");
//        infoCommand.AddOption(new Option<string>(new[] { "--id", "-i" }, "ID of the local model or source ID of external model") { IsRequired = true });
//        infoCommand.Handler = CommandHandler.Create<string?, string?, LogLevel, string>(InfoModelHandler);
//        rootCommand.AddCommand(infoCommand);

//        // Delete model command
//        var deleteCommand = new Command("delete", "Delete a model");
//        deleteCommand.AddOption(new Option<string>(new[] { "--id", "-i" }, "ID of the model to delete") { IsRequired = true });
//        deleteCommand.AddOption(new Option<bool>(new[] { "--force", "-f" }, "Force deletion without confirmation"));
//        deleteCommand.Handler = CommandHandler.Create<string?, string?, LogLevel, string, bool>(DeleteModelHandler);
//        rootCommand.AddCommand(deleteCommand);

//        // Run the command line application
//        return await rootCommand.InvokeAsync(args);
//    }

//    static async Task ListModelsHandler(string? dataPath, string? hfToken, LogLevel logLevel, string? type, string? search)
//    {
//        using var depot = CreateDepot(dataPath, hfToken, logLevel);

//        ModelType? modelType = type?.ToLowerInvariant() switch
//        {
//            "text" => ModelType.TextGeneration,
//            "embedding" => ModelType.Embedding,
//            "multimodal" => ModelType.Multimodal,
//            _ => null
//        };

//        Console.WriteLine("Loading models...");

//        var models = await depot.ListModelsAsync(modelType, search);

//        if (models.Count == 0)
//        {
//            Console.WriteLine("No models found.");
//            return;
//        }

//        Console.WriteLine($"Found {models.Count} models:");
//        Console.WriteLine();
//        Console.WriteLine($"{"ID",-40} {"Name",-30} {"Type",-15} {"Format",-12} {"Size",-12}");
//        Console.WriteLine(new string('-', 110));

//        foreach (var model in models)
//        {
//            Console.WriteLine($"{model.Id,-40} {model.Name,-30} {model.Type,-15} {model.Format,-12} {FormatSize(model.SizeInBytes),-12}");
//        }
//    }

//    static async Task SearchModelsHandler(string? dataPath, string? hfToken, LogLevel logLevel, string? type, string? query, int limit)
//    {
//        using var depot = CreateDepot(dataPath, hfToken, logLevel);

//        ModelType? modelType = type?.ToLowerInvariant() switch
//        {
//            "text" => ModelType.TextGeneration,
//            "embedding" => ModelType.Embedding,
//            "multimodal" => ModelType.Multimodal,
//            _ => null
//        };

//        Console.WriteLine($"Searching for models with query: {query}");

//        var results = await depot.SearchModelsAsync(modelType, query, limit);

//        if (results.Count == 0)
//        {
//            Console.WriteLine("No models found.");
//            return;
//        }

//        Console.WriteLine($"Found {results.Count} models:");
//        Console.WriteLine();
//        Console.WriteLine($"{"Source ID",-40} {"Name",-30} {"Type",-15} {"Source",-12} {"Status",-12}");
//        Console.WriteLine(new string('-', 110));

//        foreach (var result in results)
//        {
//            var status = result.IsDownloaded
//                ? "Downloaded"
//                : result.IsDownloading
//                    ? "Downloading"
//                    : "Available";

//            Console.WriteLine($"{result.SourceId,-40} {result.Model.Name,-30} {result.Model.Type,-15} {result.SourceName,-12} {status,-12}");
//        }
//    }

//    static async Task DownloadModelHandler(string? dataPath, string? hfToken, LogLevel logLevel, string source)
//    {
//        using var depot = CreateDepot(dataPath, hfToken, logLevel);

//        Console.WriteLine($"Getting information for model {source}...");

//        try
//        {
//            var modelInfo = await depot.GetModelInfoAsync(source);

//            Console.WriteLine($"Model: {modelInfo.Name}");
//            Console.WriteLine($"Type: {modelInfo.Type}");
//            Console.WriteLine($"Format: {modelInfo.Format}");

//            if (modelInfo.SizeInBytes > 0)
//            {
//                Console.WriteLine($"Size: {FormatSize(modelInfo.SizeInBytes)}");
//            }

//            Console.Write("Start download? [Y/n]: ");
//            var input = Console.ReadLine()?.ToLowerInvariant() ?? "y";

//            if (input != "y" && input != "yes" && !string.IsNullOrEmpty(input))
//            {
//                Console.WriteLine("Download canceled.");
//                return;
//            }

//            Console.WriteLine("Starting download...");

//            using var progress = new ConsoleProgressReporter();
//            var model = await depot.DownloadModelAsync(source, progress);

//            Console.WriteLine();
//            Console.WriteLine($"Model '{model.Name}' downloaded successfully.");
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"Error: {ex.Message}");
//        }
//    }

//    static async Task InfoModelHandler(string? dataPath, string? hfToken, LogLevel logLevel, string id)
//    {
//        using var depot = CreateDepot(dataPath, hfToken, logLevel);

//        try
//        {
//            LMModel? model = null;

//            // Check if it's a local model first
//            model = await depot.GetModelAsync(id);

//            // If not found locally, try as external source
//            if (model == null && id.Contains(':'))
//            {
//                Console.WriteLine($"Model '{id}' not found locally. Checking external source...");
//                model = await depot.GetModelInfoAsync(id);
//            }

//            if (model == null)
//            {
//                Console.WriteLine($"Model '{id}' not found.");
//                return;
//            }

//            // Display basic model information
//            Console.WriteLine("Model Information:");
//            Console.WriteLine($"ID:          {model.Id}");
//            Console.WriteLine($"Name:        {model.Name}");
//            Console.WriteLine($"Type:        {model.Type}");
//            Console.WriteLine($"Format:      {model.Format}");
//            Console.WriteLine($"Size:        {FormatSize(model.SizeInBytes)}");
//            Console.WriteLine($"Version:     {model.Version}");
//            Console.WriteLine($"Local Path:  {model.LocalPath ?? "Not downloaded"}");
//            Console.WriteLine($"Description: {model.Description}");

//            Console.WriteLine("\nCapabilities:");
//            Console.WriteLine($"Text Generation:     {(model.Capabilities.SupportsTextGeneration ? "Yes" : "No")}");
//            Console.WriteLine($"Embeddings:          {(model.Capabilities.SupportsEmbeddings ? "Yes" : "No")}");
//            Console.WriteLine($"Image Understanding: {(model.Capabilities.SupportsImageUnderstanding ? "Yes" : "No")}");
//            Console.WriteLine($"Max Context Length:  {model.Capabilities.MaxContextLength} tokens");

//            if (model.Capabilities.EmbeddingDimension.HasValue)
//            {
//                Console.WriteLine($"Embedding Dimension: {model.Capabilities.EmbeddingDimension.Value}");
//            }

//            // If we have repository information, show available artifacts
//            if (model.Repository != null && model.Repository.AvailableArtifacts.Count > 0)
//            {
//                Console.WriteLine("\nAvailable Artifacts:");
//                foreach (var artifact in model.Repository.AvailableArtifacts)
//                {
//                    string selected = (artifact.Name == model.ArtifactName) ? " (selected)" : "";
//                    Console.WriteLine($"- {artifact.Name}{selected}");
//                    Console.WriteLine($"  Format: {artifact.Format}");
//                    Console.WriteLine($"  Size: {FormatSize(artifact.SizeInBytes)}");
//                    Console.WriteLine($"  Description: {artifact.Description}");
//                    Console.WriteLine($"  Download: lmd download -s {model.Registry}:{model.RepoId}/{artifact.Name}");
//                    Console.WriteLine();
//                }
//            }
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"Error: {ex.Message}");
//        }
//    }

//    static async Task DeleteModelHandler(string? dataPath, string? hfToken, LogLevel logLevel, string id, bool force)
//    {
//        using var depot = CreateDepot(dataPath, hfToken, logLevel);

//        try
//        {
//            var model = await depot.GetModelAsync(id);

//            if (model == null)
//            {
//                Console.WriteLine($"Model '{id}' not found.");
//                return;
//            }

//            if (!force)
//            {
//                Console.Write($"Are you sure you want to delete model '{model.Name}'? [y/N]: ");
//                var input = Console.ReadLine()?.ToLowerInvariant() ?? "n";

//                if (input != "y" && input != "yes")
//                {
//                    Console.WriteLine("Deletion canceled.");
//                    return;
//                }
//            }

//            Console.WriteLine($"Deleting model '{model.Name}'...");
//            var result = await depot.DeleteModelAsync(id);

//            if (result)
//            {
//                Console.WriteLine("Model deleted successfully.");
//            }
//            else
//            {
//                Console.WriteLine("Failed to delete model.");
//            }
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"Error: {ex.Message}");
//        }
//    }

//    static LMSupplyDepot CreateDepot(string? dataPath, string? hfToken, LogLevel logLevel)
//    {
//        return new LMSupplyDepot(new LMSupplyDepotOptions
//        {
//            DataPath = dataPath,
//            HuggingFaceApiToken = hfToken,
//            ConfigureLogging = builder =>
//            {
//                // 로그 레벨이 명시적으로 지정되지 않은 경우 기본값으로 Warning 사용
//                LogLevel effectiveLogLevel = logLevel == LogLevel.Information
//                    ? LogLevel.Warning  // 기본값 변경
//                    : logLevel;

//                builder.AddConsole(configure =>
//                {
//                    // 콘솔 로거의 필터 구성
//                    configure.LogToStandardErrorThreshold = LogLevel.Error;
//                });

//                builder.SetMinimumLevel(effectiveLogLevel);

//                // 특정 네임스페이스에 대한 로그 필터링 추가
//                builder.AddFilter("LMSupplyDepots.SDK", effectiveLogLevel);
//                builder.AddFilter("Microsoft", LogLevel.Warning);
//                builder.AddFilter("System", LogLevel.Warning);
//            }
//        });
//    }

//    static string FormatSize(long bytes)
//    {
//        string[] units = { "B", "KB", "MB", "GB", "TB" };
//        double size = bytes;
//        int unitIndex = 0;

//        while (size >= 1024 && unitIndex < units.Length - 1)
//        {
//            size /= 1024;
//            unitIndex++;
//        }

//        return $"{size:0.##} {units[unitIndex]}";
//    }
//}

///// <summary>
///// Console progress reporter for model downloads.
///// </summary>
//public class ConsoleProgressReporter : IProgress<ModelDownloadProgress>, IDisposable
//{
//    private int _lastPercent = -1;
//    private bool _disposed;

//    public void Report(ModelDownloadProgress value)
//    {
//        var percent = (int)value.ProgressPercentage;

//        if (percent != _lastPercent)
//        {
//            _lastPercent = percent;

//            Console.CursorLeft = 0;

//            var progressBar = "[" + new string('#', percent / 2) + new string(' ', 50 - percent / 2) + "]";
//            var status = $"{percent}% - {FormatSize((long)value.BytesPerSecond)}/s - {value.FileName}";

//            Console.Write($"{progressBar} {status}");
//        }
//    }

//    public void Dispose()
//    {
//        if (!_disposed)
//        {
//            Console.WriteLine();
//            _disposed = true;
//        }
//    }

//    private static string FormatSize(long bytes)
//    {
//        string[] units = { "B", "KB", "MB", "GB", "TB" };
//        double size = bytes;
//        int unitIndex = 0;

//        while (size >= 1024 && unitIndex < units.Length - 1)
//        {
//            size /= 1024;
//            unitIndex++;
//        }

//        return $"{size:0.##} {units[unitIndex]}";
//    }
//}