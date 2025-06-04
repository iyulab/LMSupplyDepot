//using LMSupplyDepots.ModelHub;
//using LMSupplyDepots.ModelHub.Exceptions;
//using LMSupplyDepots.ModelHub.HuggingFace;
//using LMSupplyDepots.ModelHub.Interfaces;
//using LMSupplyDepots.ModelHub.Models;
//using LMSupplyDepots.Models;
//using Microsoft.Extensions.DependencyInjection;
//using Microsoft.Extensions.Hosting;
//using Microsoft.Extensions.Logging;

//namespace LMSupplyDepots.SampleApp;

//public class Program
//{
//    private static IHost? _host;
//    private static bool _isRunning = true;
//    private static readonly Dictionary<string, ModelDownloadState> _activeDownloads = new();

//    public static async Task Main(string[] args)
//    {
//        // Setup dependency injection
//        _host = CreateHostBuilder(args).Build();

//        Console.WriteLine("LMSupplyDepots Model Management Sample App");
//        Console.WriteLine("===========================================");

//        // Start download monitoring in background
//        var downloadMonitorTask = DownloadMonitorTask();

//        // Main menu loop
//        while (_isRunning)
//        {
//            ShowMainMenu();
//            var key = Console.ReadKey(true);
//            Console.WriteLine();

//            try
//            {
//                await HandleMainMenuInput(key.KeyChar);
//            }
//            catch (Exception ex)
//            {
//                Console.ForegroundColor = ConsoleColor.Red;
//                Console.WriteLine($"Error: {ex.Message}");
//                Console.ResetColor();
//                Console.WriteLine("Press any key to continue...");
//                Console.ReadKey(true);
//            }
//        }

//        if (_host != null)
//        {
//            await _host.StopAsync();
//        }
//    }

//    private static void ShowMainMenu()
//    {
//        Console.Clear();
//        Console.WriteLine("LMSupplyDepots Model Management Sample App");
//        Console.WriteLine("===========================================");
//        Console.WriteLine();
//        Console.WriteLine("1. List Local Models");
//        Console.WriteLine("2. Search Models (HuggingFace)");
//        Console.WriteLine("3. Model Details");
//        Console.WriteLine("4. Download Model");
//        Console.WriteLine("5. Manage Downloads");
//        Console.WriteLine("6. Delete Model");
//        Console.WriteLine();
//        Console.WriteLine("0. Exit");
//        Console.WriteLine();
//        Console.Write("Select an option: ");
//    }

//    private static async Task HandleMainMenuInput(char key)
//    {
//        switch (key)
//        {
//            case '1':
//                await ListLocalModels();
//                break;
//            case '2':
//                await SearchModels();
//                break;
//            case '3':
//                await ShowModelDetails();
//                break;
//            case '4':
//                await DownloadModel();
//                break;
//            case '5':
//                await ManageDownloads();
//                break;
//            case '6':
//                await DeleteModel();
//                break;
//            case '0':
//                _isRunning = false;
//                break;
//            default:
//                Console.WriteLine("Invalid option. Press any key to continue...");
//                Console.ReadKey(true);
//                break;
//        }
//    }

//    private static async Task ListLocalModels()
//    {
//        Console.Clear();
//        Console.WriteLine("Local Models");
//        Console.WriteLine("============");

//        var modelManager = _host!.Services.GetRequiredService<IModelManager>();
//        var models = await modelManager.ListModelsAsync();

//        if (models.Count == 0)
//        {
//            Console.WriteLine("No local models found.");
//        }
//        else
//        {
//            Console.WriteLine($"Found {models.Count} local models:");
//            Console.WriteLine();
//            Console.WriteLine($"{"ID",-60} {"Name",-40} {"Type",-15} {"Format",-10} {"Size",-10}");
//            Console.WriteLine(new string('-', 135));

//            foreach (var model in models)
//            {
//                var sizeFormatted = FormatSize(model.SizeInBytes);
//                Console.WriteLine($"{model.Id,-60} {model.Name,-40} {model.Type,-15} {model.Format,-10} {sizeFormatted,-10}");
//            }
//        }

//        Console.WriteLine();
//        Console.WriteLine("Press any key to continue...");
//        Console.ReadKey(true);
//    }

//    private static async Task SearchModels()
//    {
//        Console.Clear();
//        Console.WriteLine("Search Models");
//        Console.WriteLine("=============");

//        // Get model type filter
//        var modelTypeFilter = GetModelTypeFromUser();

//        // Get search term
//        Console.Write("Enter search term (leave empty for no filter): ");
//        var searchTerm = Console.ReadLine()?.Trim();

//        Console.WriteLine("Searching models... Please wait.");

//        var modelManager = _host!.Services.GetRequiredService<IModelManager>();
//        var searchResults = await modelManager.SearchRepositoriesAsync(
//            modelTypeFilter,
//            string.IsNullOrWhiteSpace(searchTerm) ? null : searchTerm,
//            20);

//        Console.Clear();
//        Console.WriteLine($"Search Results ({searchResults.Count} repositories found)");
//        Console.WriteLine("=================================================");

//        if (searchResults.Count == 0)
//        {
//            Console.WriteLine("No models found matching your criteria.");
//        }
//        else
//        {
//            int index = 1;
//            foreach (var repo in searchResults)
//            {
//                Console.WriteLine($"{index}. {repo.Name} ({repo.RepoId})");
//                Console.WriteLine($"   Type: {repo.Type}, Format: {repo.DefaultFormat}");
//                Console.WriteLine($"   Publisher: {repo.Publisher}");
//                Console.WriteLine($"   Description: {(repo.Description.Length > 100 ? repo.Description.Substring(0, 97) + "..." : repo.Description)}");
//                Console.WriteLine($"   Artifacts: {repo.AvailableArtifacts.Count}");
//                Console.WriteLine();
//                index++;
//            }

//            // Ask if user wants to see repository details
//            Console.WriteLine("Enter repository number to see details (or 0 to return to main menu):");
//            if (int.TryParse(Console.ReadLine(), out int repoIndex) && repoIndex > 0 && repoIndex <= searchResults.Count)
//            {
//                await ShowRepositoryDetails(searchResults[repoIndex - 1]);
//            }
//        }

//        Console.WriteLine();
//        Console.WriteLine("Press any key to continue...");
//        Console.ReadKey(true);
//    }

//    private static async Task ShowRepositoryDetails(LMRepo repo)
//    {
//        Console.Clear();
//        Console.WriteLine($"Repository Details: {repo.Name}");
//        Console.WriteLine("===================" + new string('=', repo.Name.Length));
//        Console.WriteLine($"ID: {repo.Id}");
//        Console.WriteLine($"Registry: {repo.Registry}");
//        Console.WriteLine($"RepoId: {repo.RepoId}");
//        Console.WriteLine($"Publisher: {repo.Publisher}");
//        Console.WriteLine($"Type: {repo.Type}");
//        Console.WriteLine($"Default Format: {repo.DefaultFormat}");
//        Console.WriteLine($"Version: {repo.Version}");
//        Console.WriteLine();
//        Console.WriteLine("Description:");
//        Console.WriteLine(repo.Description);
//        Console.WriteLine();

//        Console.WriteLine("Available Artifacts:");
//        Console.WriteLine("-------------------");
//        int index = 1;

//        foreach (var artifact in repo.AvailableArtifacts)
//        {
//            Console.WriteLine($"{index}. {artifact.Name}");
//            Console.WriteLine($"   Format: {artifact.Format}");
//            Console.WriteLine($"   Size: {FormatSize(artifact.SizeInBytes)}");
//            Console.WriteLine($"   Description: {artifact.Description}");
//            if (artifact.QuantizationBits.HasValue)
//            {
//                Console.WriteLine($"   Quantization: {artifact.QuantizationBits} bits");
//            }
//            if (!string.IsNullOrEmpty(artifact.SizeCategory))
//            {
//                Console.WriteLine($"   Size Category: {artifact.SizeCategory}");
//            }
//            Console.WriteLine();
//            index++;
//        }

//        // Ask if user wants to download an artifact
//        Console.WriteLine("Enter artifact number to download (or 0 to return to previous menu):");
//        if (int.TryParse(Console.ReadLine(), out int artifactIndex) && artifactIndex > 0 && artifactIndex <= repo.AvailableArtifacts.Count)
//        {
//            var artifact = repo.AvailableArtifacts[artifactIndex - 1];

//            // Generate the model ID
//            string modelId = $"{repo.Registry}:{repo.RepoId}/{artifact.Name}";

//            await DownloadSpecificModel(modelId);
//        }
//    }

//    private static async Task ShowModelDetails()
//    {
//        Console.Clear();
//        Console.WriteLine("Model Details");
//        Console.WriteLine("=============");

//        Console.Write("Enter model ID: ");
//        var modelId = Console.ReadLine()?.Trim();

//        if (string.IsNullOrWhiteSpace(modelId))
//        {
//            Console.WriteLine("Invalid model ID.");
//            return;
//        }

//        var modelManager = _host!.Services.GetRequiredService<IModelManager>();

//        // Try to get local model first
//        var model = await modelManager.GetModelAsync(modelId);

//        if (model == null)
//        {
//            // If not found locally, try to get info from external source
//            Console.WriteLine("Model not found locally. Trying to fetch info from external source...");
//            try
//            {
//                model = await modelManager.GetExternalModelInfoAsync(modelId);
//                Console.WriteLine("Found model information from external source.");
//            }
//            catch (Exception ex)
//            {
//                Console.WriteLine($"Error fetching model information: {ex.Message}");
//                Console.WriteLine("Press any key to continue...");
//                Console.ReadKey(true);
//                return;
//            }
//        }

//        Console.WriteLine();
//        Console.WriteLine($"Model ID: {model.Id}");
//        Console.WriteLine($"Name: {model.Name}");
//        Console.WriteLine($"Registry: {model.Registry}");
//        Console.WriteLine($"RepoId: {model.RepoId}");
//        Console.WriteLine($"Type: {model.Type}");
//        Console.WriteLine($"Format: {model.Format}");
//        Console.WriteLine($"Version: {model.Version}");
//        Console.WriteLine($"Size: {FormatSize(model.SizeInBytes)}");
//        Console.WriteLine($"Artifact: {model.ArtifactName}");
//        Console.WriteLine();

//        Console.WriteLine("Capabilities:");
//        Console.WriteLine($"- Text Generation: {model.Capabilities.SupportsTextGeneration}");
//        Console.WriteLine($"- Embeddings: {model.Capabilities.SupportsEmbeddings}");
//        Console.WriteLine($"- Image Understanding: {model.Capabilities.SupportsImageUnderstanding}");
//        Console.WriteLine($"- Max Context Length: {model.Capabilities.MaxContextLength}");
//        if (model.Capabilities.EmbeddingDimension.HasValue)
//        {
//            Console.WriteLine($"- Embedding Dimension: {model.Capabilities.EmbeddingDimension}");
//        }

//        Console.WriteLine();
//        Console.WriteLine("Description:");
//        Console.WriteLine(model.Description);

//        if (model.IsLocal)
//        {
//            Console.WriteLine();
//            Console.WriteLine($"Local Path: {model.LocalPath}");
//            if (model.FilePaths.Count > 0)
//            {
//                Console.WriteLine();
//                Console.WriteLine("Files:");
//                foreach (var file in model.FilePaths)
//                {
//                    Console.WriteLine($"- {Path.GetFileName(file)}");
//                }
//            }
//        }
//        else
//        {
//            Console.WriteLine();
//            Console.WriteLine("This model is not downloaded locally.");
//            Console.WriteLine("Would you like to download it? (Y/N)");

//            var key = Console.ReadKey(true);
//            if (key.Key == ConsoleKey.Y)
//            {
//                await DownloadSpecificModel(model.Id);
//                return;
//            }
//        }

//        Console.WriteLine();
//        Console.WriteLine("Press any key to continue...");
//        Console.ReadKey(true);
//    }

//    private static async Task DownloadModel()
//    {
//        Console.Clear();
//        Console.WriteLine("Download Model");
//        Console.WriteLine("==============");

//        Console.Write("Enter model ID to download: ");
//        var modelId = Console.ReadLine()?.Trim();

//        if (string.IsNullOrWhiteSpace(modelId))
//        {
//            Console.WriteLine("Invalid model ID.");
//            return;
//        }

//        await DownloadSpecificModel(modelId);
//    }

//    private static async Task DownloadSpecificModel(string modelId)
//    {
//        var modelManager = _host!.Services.GetRequiredService<IModelManager>();

//        // Check if model is already downloaded
//        bool isDownloaded = await modelManager.IsModelDownloadedAsync(modelId);
//        if (isDownloaded)
//        {
//            Console.WriteLine("This model is already downloaded.");
//            Console.WriteLine("Press any key to continue...");
//            Console.ReadKey(true);
//            return;
//        }

//        // Check if download is already in progress
//        var downloadStatus = modelManager.GetDownloadStatus(modelId);
//        if (downloadStatus == ModelDownloadStatus.Downloading)
//        {
//            Console.WriteLine("This model is already being downloaded.");
//            Console.WriteLine("Press any key to continue...");
//            Console.ReadKey(true);
//            return;
//        }

//        // Get model info first to show user what they're downloading
//        try
//        {
//            var modelInfo = await modelManager.GetExternalModelInfoAsync(modelId);

//            Console.WriteLine($"Preparing to download model: {modelInfo.Name}");
//            Console.WriteLine($"Type: {modelInfo.Type}");
//            Console.WriteLine($"Format: {modelInfo.Format}");
//            Console.WriteLine($"Size: {FormatSize(modelInfo.SizeInBytes)}");
//            Console.WriteLine($"Artifact: {modelInfo.ArtifactName}");
//            Console.WriteLine();
//            Console.WriteLine("Do you want to proceed with the download? (Y/N)");

//            var key = Console.ReadKey(true);
//            if (key.Key != ConsoleKey.Y)
//            {
//                Console.WriteLine("Download cancelled.");
//                Console.WriteLine("Press any key to continue...");
//                Console.ReadKey(true);
//                return;
//            }

//            Console.WriteLine("Starting download...");

//            // Create progress reporter
//            var progress = new Progress<ModelDownloadProgress>(p =>
//            {
//                // Progress updates will be handled by the download monitor
//            });

//            // Start the download
//            var downloadTask = modelManager.DownloadModelAsync(modelId, progress);

//            // Store the model ID for tracking
//            Console.WriteLine("Download initiated. You can monitor progress from the 'Manage Downloads' menu.");
//            Console.WriteLine("Press any key to continue...");
//            Console.ReadKey(true);
//        }
//        catch (ModelDownloadException ex) when (ex.IsAuthenticationError)
//        {
//            Console.ForegroundColor = ConsoleColor.Yellow;
//            Console.WriteLine("Authentication Error:");
//            Console.WriteLine(ex.GetUserFriendlyMessage());
//            Console.WriteLine();
//            Console.WriteLine("This model requires a valid HuggingFace API token. Please set your token in the application settings.");
//            Console.WriteLine("You can obtain a token from https://huggingface.co/settings/tokens");
//            Console.ResetColor();
//            Console.WriteLine("Press any key to continue...");
//            Console.ReadKey(true);
//        }
//        catch (Exception ex)
//        {
//            Console.ForegroundColor = ConsoleColor.Red;
//            Console.WriteLine($"Error starting download: {ex.Message}");
//            Console.ResetColor();
//            Console.WriteLine("Press any key to continue...");
//            Console.ReadKey(true);
//        }
//    }

//    private static async Task ManageDownloads()
//    {
//        while (true)
//        {
//            Console.Clear();
//            Console.WriteLine("Manage Downloads");
//            Console.WriteLine("===============");

//            var modelManager = _host!.Services.GetRequiredService<IModelManager>();
//            var activeDownloads = modelManager.GetActiveDownloads();

//            if (activeDownloads.Count == 0)
//            {
//                Console.WriteLine("No active downloads.");
//                Console.WriteLine("Press any key to return to main menu...");
//                Console.ReadKey(true);
//                return;
//            }

//            Console.WriteLine($"Active Downloads: {activeDownloads.Count}");
//            Console.WriteLine();

//            int index = 1;
//            var downloadIds = new List<string>();

//            foreach (var download in activeDownloads)
//            {
//                var state = download.Value;
//                downloadIds.Add(download.Key);

//                var progress = state.ProgressPercentage;
//                var status = state.Status.ToString();
//                var size = state.TotalBytes.HasValue ? FormatSize(state.TotalBytes.Value) : "Unknown";
//                var downloaded = FormatSize(state.BytesDownloaded);
//                var speed = FormatSize((long)state.AverageSpeed) + "/s";
//                var eta = state.EstimatedTimeRemaining.HasValue ? $"{state.EstimatedTimeRemaining.Value.TotalMinutes:F1} min" : "Unknown";

//                Console.WriteLine($"{index}. {Path.GetFileName(state.TargetDirectory)}");
//                Console.WriteLine($"   Status: {status}");
//                Console.WriteLine($"   Progress: {progress:F1}% ({downloaded} / {size})");
//                Console.WriteLine($"   Speed: {speed}, ETA: {eta}");
//                Console.WriteLine();

//                index++;
//            }

//            Console.WriteLine("Options:");
//            Console.WriteLine("P. Pause a download");
//            Console.WriteLine("R. Resume a download");
//            Console.WriteLine("C. Cancel a download");
//            Console.WriteLine("X. Return to main menu");
//            Console.WriteLine();
//            Console.Write("Enter option: ");

//            var key = Console.ReadKey(true);
//            Console.WriteLine();

//            if (key.Key == ConsoleKey.X)
//            {
//                return;
//            }

//            if (key.Key == ConsoleKey.P || key.Key == ConsoleKey.R || key.Key == ConsoleKey.C)
//            {
//                Console.Write("Enter download number: ");
//                if (int.TryParse(Console.ReadLine(), out int downloadIndex) &&
//                    downloadIndex > 0 && downloadIndex <= downloadIds.Count)
//                {
//                    var downloadId = downloadIds[downloadIndex - 1];

//                    switch (key.Key)
//                    {
//                        case ConsoleKey.P:
//                            await PauseDownload(downloadId);
//                            break;
//                        case ConsoleKey.R:
//                            await ResumeDownload(downloadId);
//                            break;
//                        case ConsoleKey.C:
//                            await CancelDownload(downloadId);
//                            break;
//                    }
//                }
//                else
//                {
//                    Console.WriteLine("Invalid download number.");
//                    Console.WriteLine("Press any key to continue...");
//                    Console.ReadKey(true);
//                }
//            }
//        }
//    }

//    private static async Task PauseDownload(string modelId)
//    {
//        var modelManager = _host!.Services.GetRequiredService<IModelManager>();

//        try
//        {
//            bool result = await modelManager.PauseDownloadAsync(modelId);
//            if (result)
//            {
//                Console.WriteLine("Download paused successfully.");
//            }
//            else
//            {
//                Console.WriteLine("Failed to pause download.");
//            }
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"Error pausing download: {ex.Message}");
//        }

//        Console.WriteLine("Press any key to continue...");
//        Console.ReadKey(true);
//    }

//    private static async Task ResumeDownload(string modelId)
//    {
//        var modelManager = _host!.Services.GetRequiredService<IModelManager>();

//        try
//        {
//            var progress = new Progress<ModelDownloadProgress>(p =>
//            {
//                // Progress updates will be handled by the download monitor
//            });

//            var result = await modelManager.ResumeDownloadAsync(modelId, progress);
//            if (result != null)
//            {
//                Console.WriteLine("Download resumed successfully.");
//            }
//            else
//            {
//                Console.WriteLine("Failed to resume download.");
//            }
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"Error resuming download: {ex.Message}");
//        }

//        Console.WriteLine("Press any key to continue...");
//        Console.ReadKey(true);
//    }

//    private static async Task CancelDownload(string modelId)
//    {
//        var modelManager = _host!.Services.GetRequiredService<IModelManager>();

//        try
//        {
//            bool result = await modelManager.CancelDownloadAsync(modelId);
//            if (result)
//            {
//                Console.WriteLine("Download cancelled successfully.");
//            }
//            else
//            {
//                Console.WriteLine("Failed to cancel download.");
//            }
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"Error cancelling download: {ex.Message}");
//        }

//        Console.WriteLine("Press any key to continue...");
//        Console.ReadKey(true);
//    }

//    private static async Task DeleteModel()
//    {
//        Console.Clear();
//        Console.WriteLine("Delete Model");
//        Console.WriteLine("============");

//        Console.Write("Enter model ID to delete: ");
//        var modelId = Console.ReadLine()?.Trim();

//        if (string.IsNullOrWhiteSpace(modelId))
//        {
//            Console.WriteLine("Invalid model ID.");
//            return;
//        }

//        var modelManager = _host!.Services.GetRequiredService<IModelManager>();

//        // Check if model exists
//        var model = await modelManager.GetModelAsync(modelId);
//        if (model == null)
//        {
//            Console.WriteLine("Model not found.");
//            Console.WriteLine("Press any key to continue...");
//            Console.ReadKey(true);
//            return;
//        }

//        Console.WriteLine($"You are about to delete model: {model.Name}");
//        Console.WriteLine($"ID: {model.Id}");
//        Console.WriteLine($"Type: {model.Type}");
//        Console.WriteLine($"Size: {FormatSize(model.SizeInBytes)}");
//        Console.WriteLine();
//        Console.WriteLine("This action cannot be undone!");
//        Console.WriteLine("Are you sure you want to delete this model? (Y/N)");

//        var key = Console.ReadKey(true);
//        if (key.Key != ConsoleKey.Y)
//        {
//            Console.WriteLine("Deletion cancelled.");
//            Console.WriteLine("Press any key to continue...");
//            Console.ReadKey(true);
//            return;
//        }

//        try
//        {
//            bool result = await modelManager.DeleteModelAsync(modelId);
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
//            Console.WriteLine($"Error deleting model: {ex.Message}");
//        }

//        Console.WriteLine("Press any key to continue...");
//        Console.ReadKey(true);
//    }

//    private static async Task DownloadMonitorTask()
//    {
//        var modelManager = _host!.Services.GetRequiredService<IModelManager>();

//        while (_isRunning)
//        {
//            try
//            {
//                // Get all active downloads
//                var activeDownloads = modelManager.GetActiveDownloads();

//                foreach (var download in activeDownloads)
//                {
//                    // Update our tracking dictionary
//                    _activeDownloads[download.Key] = download.Value;
//                }

//                // Check for completed downloads to remove them from our tracking
//                var completedKeys = _activeDownloads.Keys
//                    .Where(k => !activeDownloads.ContainsKey(k))
//                    .ToList();

//                foreach (var key in completedKeys)
//                {
//                    _activeDownloads.Remove(key);
//                }
//            }
//            catch (Exception ex)
//            {
//                // Just swallow exceptions in the monitor task
//            }

//            // Wait a bit before checking again
//            await Task.Delay(1000);
//        }
//    }

//    private static ModelType? GetModelTypeFromUser()
//    {
//        Console.WriteLine("Select model type:");
//        Console.WriteLine("1. Text Generation");
//        Console.WriteLine("2. Embedding");
//        Console.WriteLine("3. Multimodal");
//        Console.WriteLine("4. Any Type");
//        Console.Write("Enter option (1-4): ");

//        if (int.TryParse(Console.ReadLine(), out int option))
//        {
//            return option switch
//            {
//                1 => ModelType.TextGeneration,
//                2 => ModelType.Embedding,
//                _ => null
//            };
//        }

//        return null;
//    }

//    private static string FormatSize(long? bytes)
//    {
//        if (!bytes.HasValue || bytes.Value < 0)
//            return "Unknown";

//        string[] units = { "B", "KB", "MB", "GB", "TB" };
//        double size = bytes.Value;
//        int unitIndex = 0;

//        while (size >= 1024 && unitIndex < units.Length - 1)
//        {
//            size /= 1024;
//            unitIndex++;
//        }

//        return $"{size:F2} {units[unitIndex]}";
//    }

//    private static IHostBuilder CreateHostBuilder(string[] args) =>
//        Host.CreateDefaultBuilder(args)
//            .ConfigureLogging(logging =>
//            {
//                logging.ClearProviders();
//                logging.AddConsole();
//                logging.SetMinimumLevel(LogLevel.Warning);
//            })
//            .ConfigureServices((hostContext, services) =>
//            {
//                // Register LMSupplyDepots services
//                services.AddModelHub(options =>
//                {
//                    options.ModelsDirectory = Path.Combine(
//                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
//                        "LMSupplyDepots");
//                });

//                // Add HuggingFace downloader
//                services.AddHuggingFaceDownloader(options =>
//                {
//                    // Add your HuggingFace API token here if you have one
//                    options.ApiToken = Environment.GetEnvironmentVariable("HUGGINGFACE_TOKEN");
//                    options.MaxConcurrentFileDownloads = 4;
//                });
//            });
//}