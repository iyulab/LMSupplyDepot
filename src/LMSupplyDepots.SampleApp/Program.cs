using LMSupplyDepots.Models;
using LMSupplyDepots.SDK;
using Microsoft.Extensions.Logging;

namespace LMSupplyDepots.SampleApp;

/// <summary>
/// Sample application - Model loading and inference example
/// </summary>
public class Program
{
    private static LMSupplyDepot? _depot;
    private static bool _isRunning = true;
    private static string? _loadedModelId = null;

    public static async Task Main(string[] args)
    {
        // Logger setup
        using var logFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Models directory path
        var dataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LMSupplyDepots");

        // Initialize LMSupplyDepot
        _depot = new LMSupplyDepot(new LMSupplyDepotOptions
        {
            DataPath = dataPath,
            EnableModelCaching = true,
            MaxCachedModels = 2,
            ForceCpuOnly = true, // Force CPU-only mode to avoid GPU-related errors
            LLamaOptions = new LLamaOptions
            {
                Threads = Environment.ProcessorCount, // Automatically set CPU thread count
                GpuLayers = 0, // Disable GPU offload
                ContextSize = 4096, // Set context size
                UseMemoryMapping = true // Use memory mapping for better performance
            }
        }, logFactory);

        Console.WriteLine("LMSupplyDepots Inference Sample App");
        Console.WriteLine("==================================");

        // Main menu loop
        while (_isRunning)
        {
            ShowMainMenu();
            var key = Console.ReadKey(true);
            Console.WriteLine();

            try
            {
                await HandleMainMenuInput(key.KeyChar);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"InnerException: {ex.InnerException.Message}");
                    Console.WriteLine($"InnerException StackTrace: {ex.InnerException.StackTrace}");
                }
                Console.ResetColor();
                Console.WriteLine("Press any key to continue...");
                Console.ReadKey(true);
            }
        }

        if (_depot != null)
        {
            _depot.Dispose();
        }
    }

    private static void ShowMainMenu()
    {
        Console.Clear();
        Console.WriteLine("LMSupplyDepots Inference Sample App");
        Console.WriteLine("==================================");
        Console.WriteLine();

        if (_loadedModelId != null)
        {
            Console.WriteLine($"Currently loaded model: {_loadedModelId}");
            Console.WriteLine();
        }

        Console.WriteLine("1. Load Model");
        Console.WriteLine("2. View Downloaded Models");
        Console.WriteLine("3. Generate Text");
        Console.WriteLine("4. Generate Text (Streaming)");
        Console.WriteLine("5. Generate Embeddings");
        Console.WriteLine("6. Unload Model");
        Console.WriteLine();
        Console.WriteLine("0. Exit");
        Console.WriteLine();
        Console.Write("Select option: ");
    }

    private static async Task HandleMainMenuInput(char key)
    {
        switch (key)
        {
            case '1':
                await LoadModel();
                break;
            case '2':
                await ShowDownloadedModels();
                break;
            case '3':
                await GenerateText();
                break;
            case '4':
                await GenerateTextStreaming();
                break;
            case '5':
                await GenerateEmbeddings();
                break;
            case '6':
                await UnloadModel();
                break;
            case '0':
                _isRunning = false;
                break;
            default:
                Console.WriteLine("Invalid option. Press any key to continue...");
                Console.ReadKey(true);
                break;
        }
    }

    private static async Task LoadModel()
    {
        Console.Clear();
        Console.WriteLine("Load Model");
        Console.WriteLine("========");

        // Show list of downloaded models
        var downloadedModels = await _depot!.ListModelsAsync();
        if (downloadedModels.Count > 0)
        {
            Console.WriteLine("Downloaded Models:");
            Console.WriteLine("-------------------");

            int index = 1;
            foreach (var model in downloadedModels)
            {
                Console.WriteLine($"{index}. ID: {model.Id}");
                Console.WriteLine($"   Name: {model.Name}");
                Console.WriteLine($"   Type: {model.Type}");
                Console.WriteLine($"   Format: {model.Format}");
                Console.WriteLine();
                index++;
            }

            Console.WriteLine("Enter the number of a downloaded model or type 'c' to enter custom ID or path:");
            var input = Console.ReadLine()?.Trim().ToLower();

            if (!string.IsNullOrEmpty(input) && int.TryParse(input, out int modelIndex) &&
                modelIndex > 0 && modelIndex <= downloadedModels.Count)
            {
                // Load the selected model
                var selectedModel = downloadedModels[modelIndex - 1];
                await LoadModelById(selectedModel.Id);
                return;
            }
            else if (input != "c")
            {
                Console.WriteLine("Invalid input.");
                Console.WriteLine("Press any key to continue...");
                Console.ReadKey(true);
                return;
            }
        }

        Console.WriteLine("Select one of the following:");
        Console.WriteLine("1. Enter Model ID");
        Console.WriteLine("2. Enter Model File Path");
        Console.Write("Select option: ");

        var key = Console.ReadKey(true);
        Console.WriteLine(key.KeyChar);
        Console.WriteLine();

        if (key.KeyChar == '1')
        {
            Console.Write("Enter Model ID: ");
            var modelId = Console.ReadLine()?.Trim();

            if (!string.IsNullOrEmpty(modelId))
            {
                await LoadModelById(modelId);
            }
            else
            {
                Console.WriteLine("Please enter a valid model ID.");
                Console.WriteLine("Press any key to continue...");
                Console.ReadKey(true);
            }
        }
        else if (key.KeyChar == '2')
        {
            Console.Write("Enter Model File Path: ");
            var modelPath = Console.ReadLine()?.Trim();

            if (!string.IsNullOrEmpty(modelPath) && File.Exists(modelPath))
            {
                // Select model type
                Console.WriteLine("Select model type:");
                Console.WriteLine("1. Text Generation");
                Console.WriteLine("2. Embedding");
                Console.Write("Select option: ");

                var typeKey = Console.ReadKey(true);
                Console.WriteLine(typeKey.KeyChar);

                var modelType = typeKey.KeyChar == '2' ?
                    ModelType.Embedding : ModelType.TextGeneration;

                // This part informs about SDK limitation for direct local file loading
                Console.WriteLine();
                Console.WriteLine("Sorry, the current SDK version does not support loading models directly from file paths.");
                Console.WriteLine("You can use one of the following methods:");
                Console.WriteLine("1. Copy the model to the LMSupplyDepots directory and register it through the SDK");
                Console.WriteLine("2. Use a HuggingFace model ID to download and then load the model");
                Console.WriteLine();
                Console.WriteLine("Press any key to continue...");
                Console.ReadKey(true);
            }
            else
            {
                Console.WriteLine("Invalid file path.");
                Console.WriteLine("Press any key to continue...");
                Console.ReadKey(true);
            }
        }
        else
        {
            Console.WriteLine("Invalid option.");
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey(true);
        }
    }

    private static async Task LoadModelById(string modelId)
    {
        Console.WriteLine($"Loading model {modelId}...");

        try
        {
            // Set loading parameters - force CPU only
            var parameters = new Dictionary<string, object?>
            {
                ["threads"] = Environment.ProcessorCount,
                ["gpu_layers"] = 0 // Force CPU only
            };

            var model = await _depot!.LoadModelAsync(modelId, parameters);
            _loadedModelId = modelId;

            Console.WriteLine("Model loaded successfully:");
            Console.WriteLine($"Name: {model.Name}");
            Console.WriteLine($"Type: {model.Type}");
            Console.WriteLine($"Format: {model.Format}");

            if (model.Capabilities != null)
            {
                Console.WriteLine("Capabilities:");
                Console.WriteLine($"- Text Generation: {model.Capabilities.SupportsTextGeneration}");
                Console.WriteLine($"- Embeddings: {model.Capabilities.SupportsEmbeddings}");
                Console.WriteLine($"- Max Context Length: {model.Capabilities.MaxContextLength}");
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to load model: {ex.Message}", ex);
        }

        Console.WriteLine();
        Console.WriteLine("Press any key to continue...");
        Console.ReadKey(true);
    }

    private static async Task ShowDownloadedModels()
    {
        Console.Clear();
        Console.WriteLine("Downloaded Models");
        Console.WriteLine("================");

        try
        {
            var models = await _depot!.ListModelsAsync();

            if (models.Count == 0)
            {
                Console.WriteLine("No downloaded models found.");
            }
            else
            {
                Console.WriteLine($"Found {models.Count} downloaded models:");
                Console.WriteLine();

                foreach (var model in models)
                {
                    Console.WriteLine($"ID: {model.Id}");
                    Console.WriteLine($"Name: {model.Name}");
                    Console.WriteLine($"Type: {model.Type}");
                    Console.WriteLine($"Format: {model.Format}");
                    Console.WriteLine($"Size: {FormatSize(model.SizeInBytes)}");

                    if (model.Capabilities != null)
                    {
                        Console.WriteLine("Capabilities:");
                        Console.WriteLine($"- Text Generation: {model.Capabilities.SupportsTextGeneration}");
                        Console.WriteLine($"- Embeddings: {model.Capabilities.SupportsEmbeddings}");
                        if (model.Capabilities.EmbeddingDimension.HasValue)
                        {
                            Console.WriteLine($"- Embedding Dimension: {model.Capabilities.EmbeddingDimension}");
                        }
                        Console.WriteLine($"- Max Context Length: {model.Capabilities.MaxContextLength}");
                    }

                    if (!string.IsNullOrEmpty(model.LocalPath))
                    {
                        Console.WriteLine($"Local Path: {model.LocalPath}");
                    }

                    Console.WriteLine();
                }

                // Offer option to load a model
                Console.WriteLine("Do you want to load a model? (Y/N)");
                var key = Console.ReadKey(true);
                Console.WriteLine(key.KeyChar);

                if (key.KeyChar == 'y' || key.KeyChar == 'Y')
                {
                    Console.Write($"Enter the number of the model to load (1-{models.Count}): ");
                    if (int.TryParse(Console.ReadLine(), out int modelIndex) &&
                        modelIndex > 0 && modelIndex <= models.Count)
                    {
                        var selectedModel = models[modelIndex - 1];
                        await LoadModelById(selectedModel.Id);
                    }
                    else
                    {
                        Console.WriteLine("Invalid model number.");
                        Console.WriteLine("Press any key to continue...");
                        Console.ReadKey(true);
                    }
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to list models: {ex.Message}", ex);
        }

        Console.WriteLine();
        Console.WriteLine("Press any key to continue...");
        Console.ReadKey(true);
    }

    private static async Task ListModels()
    {
        Console.Clear();
        Console.WriteLine("Loaded Models");
        Console.WriteLine("==============");

        var loadedModels = await _depot!.GetLoadedModelsAsync();

        if (loadedModels.Count == 0)
        {
            Console.WriteLine("No loaded models.");
        }
        else
        {
            foreach (var model in loadedModels)
            {
                Console.WriteLine($"ID: {model.Id}");
                Console.WriteLine($"Name: {model.Name}");
                Console.WriteLine($"Type: {model.Type}");
                Console.WriteLine($"Format: {model.Format}");

                if (model.Capabilities != null)
                {
                    Console.WriteLine("Capabilities:");
                    Console.WriteLine($"- Text Generation: {model.Capabilities.SupportsTextGeneration}");
                    Console.WriteLine($"- Embeddings: {model.Capabilities.SupportsEmbeddings}");
                }

                Console.WriteLine();
            }
        }

        Console.WriteLine();
        Console.WriteLine("Press any key to continue...");
        Console.ReadKey(true);
    }

    private static async Task GenerateText()
    {
        if (_loadedModelId == null)
        {
            Console.WriteLine("Please load a model first.");
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey(true);
            return;
        }

        Console.Clear();
        Console.WriteLine("Generate Text");
        Console.WriteLine("==========");

        Console.WriteLine("Enter your prompt (multiple lines allowed, enter blank line when done):");
        var promptBuilder = new System.Text.StringBuilder();
        string? line;
        while (!string.IsNullOrWhiteSpace(line = Console.ReadLine()))
        {
            promptBuilder.AppendLine(line);
        }

        var prompt = promptBuilder.ToString().Trim();
        if (string.IsNullOrEmpty(prompt))
        {
            Console.WriteLine("Please enter a valid prompt.");
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey(true);
            return;
        }

        Console.WriteLine();
        Console.WriteLine("Generation parameters:");

        Console.Write("Max tokens (default: 512): ");
        var maxTokensInput = Console.ReadLine();
        int maxTokens = string.IsNullOrEmpty(maxTokensInput) ? 512 : int.Parse(maxTokensInput);

        Console.Write("Temperature (0.0-2.0, default: 0.7): ");
        var temperatureInput = Console.ReadLine();
        float temperature = string.IsNullOrEmpty(temperatureInput) ? 0.7f : float.Parse(temperatureInput);

        Console.Write("Top-P (0.0-1.0, default: 0.9): ");
        var topPInput = Console.ReadLine();
        float topP = string.IsNullOrEmpty(topPInput) ? 0.9f : float.Parse(topPInput);

        Console.WriteLine();
        Console.WriteLine("Generating text...");
        Console.WriteLine();

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var response = await _depot!.GenerateTextAsync(
                _loadedModelId,
                prompt,
                maxTokens,
                temperature,
                topP);

            stopwatch.Stop();

            Console.WriteLine("Generated Text:");
            Console.WriteLine("=============");
            Console.WriteLine(response.Text);
            Console.WriteLine();
            Console.WriteLine($"Token count: {response.OutputTokens}");
            Console.WriteLine($"Time elapsed: {response.ElapsedTime.TotalSeconds:F2} seconds");
            Console.WriteLine($"Speed: {response.OutputTokens / response.ElapsedTime.TotalSeconds:F2} tokens/second");
        }
        catch (Exception ex)
        {
            throw new Exception($"Text generation failed: {ex.Message}", ex);
        }

        Console.WriteLine();
        Console.WriteLine("Press any key to continue...");
        Console.ReadKey(true);
    }

    private static async Task GenerateTextStreaming()
    {
        if (_loadedModelId == null)
        {
            Console.WriteLine("Please load a model first.");
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey(true);
            return;
        }

        Console.Clear();
        Console.WriteLine("Generate Text (Streaming)");
        Console.WriteLine("===================");

        Console.WriteLine("Enter your prompt (multiple lines allowed, enter blank line when done):");
        var promptBuilder = new System.Text.StringBuilder();
        string? line;
        while (!string.IsNullOrWhiteSpace(line = Console.ReadLine()))
        {
            promptBuilder.AppendLine(line);
        }

        var prompt = promptBuilder.ToString().Trim();
        if (string.IsNullOrEmpty(prompt))
        {
            Console.WriteLine("Please enter a valid prompt.");
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey(true);
            return;
        }

        Console.WriteLine();
        Console.WriteLine("Generation parameters:");

        Console.Write("Max tokens (default: 512): ");
        var maxTokensInput = Console.ReadLine();
        int maxTokens = string.IsNullOrEmpty(maxTokensInput) ? 512 : int.Parse(maxTokensInput);

        Console.Write("Temperature (0.0-2.0, default: 0.7): ");
        var temperatureInput = Console.ReadLine();
        float temperature = string.IsNullOrEmpty(temperatureInput) ? 0.7f : float.Parse(temperatureInput);

        Console.Write("Top-P (0.0-1.0, default: 0.9): ");
        var topPInput = Console.ReadLine();
        float topP = string.IsNullOrEmpty(topPInput) ? 0.9f : float.Parse(topPInput);

        Console.WriteLine();
        Console.WriteLine("Generated Text (Streaming):");
        Console.WriteLine("=======================");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var tokenCount = 0;

        try
        {
            await foreach (var token in _depot!.GenerateTextStreamAsync(
                _loadedModelId,
                prompt,
                maxTokens,
                temperature,
                topP))
            {
                Console.Write(token);
                tokenCount++;
            }

            stopwatch.Stop();

            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine($"Token count: {tokenCount}");
            Console.WriteLine($"Time elapsed: {stopwatch.Elapsed.TotalSeconds:F2} seconds");
            Console.WriteLine($"Speed: {tokenCount / stopwatch.Elapsed.TotalSeconds:F2} tokens/second");
        }
        catch (Exception ex)
        {
            throw new Exception($"Text generation failed: {ex.Message}", ex);
        }

        Console.WriteLine();
        Console.WriteLine("Press any key to continue...");
        Console.ReadKey(true);
    }

    private static async Task GenerateEmbeddings()
    {
        if (_loadedModelId == null)
        {
            Console.WriteLine("Please load a model first.");
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey(true);
            return;
        }

        Console.Clear();
        Console.WriteLine("Generate Embeddings");
        Console.WriteLine("==========");

        Console.WriteLine("Enter texts to embed (one per line, enter blank line when done):");
        var texts = new List<string>();
        string? line;
        while (!string.IsNullOrWhiteSpace(line = Console.ReadLine()))
        {
            texts.Add(line.Trim());
        }

        if (texts.Count == 0)
        {
            Console.WriteLine("Please enter valid texts.");
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey(true);
            return;
        }

        Console.WriteLine();
        Console.Write("Normalize vectors (y/n, default: y): ");
        var normalizeInput = Console.ReadLine()?.ToLower();
        bool normalize = normalizeInput != "n";

        Console.WriteLine();
        Console.WriteLine("Generating embeddings...");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var response = await _depot!.GenerateEmbeddingsAsync(
                _loadedModelId,
                texts,
                normalize);

            stopwatch.Stop();

            Console.WriteLine("Embeddings generated:");
            Console.WriteLine($"Number of embeddings: {response.Embeddings.Count}");
            Console.WriteLine($"Dimensions: {response.Dimension}");
            Console.WriteLine($"Token count: {response.TotalTokens}");
            Console.WriteLine($"Time elapsed: {response.ElapsedTime.TotalSeconds:F2} seconds");

            // Show embedding samples
            Console.WriteLine();
            Console.WriteLine("First embedding (first 5 values):");
            for (int i = 0; i < Math.Min(5, response.Dimension); i++)
            {
                Console.WriteLine($"[{i}]: {response.Embeddings[0][i]}");
            }

            // Calculate similarity (if there are at least 2 texts)
            if (response.Embeddings.Count >= 2)
            {
                Console.WriteLine();
                Console.WriteLine("Cosine similarities between embeddings:");

                for (int i = 0; i < response.Embeddings.Count; i++)
                {
                    for (int j = i + 1; j < response.Embeddings.Count; j++)
                    {
                        var similarity = CalculateCosineSimilarity(
                            response.Embeddings[i],
                            response.Embeddings[j]);

                        Console.WriteLine($"Similarity between text {i + 1} and text {j + 1}: {similarity:F4}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"Embedding generation failed: {ex.Message}", ex);
        }

        Console.WriteLine();
        Console.WriteLine("Press any key to continue...");
        Console.ReadKey(true);
    }

    private static async Task UnloadModel()
    {
        if (_loadedModelId == null)
        {
            Console.WriteLine("No model is currently loaded.");
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey(true);
            return;
        }

        Console.Clear();
        Console.WriteLine("Unload Model");
        Console.WriteLine("==========");

        Console.WriteLine($"Do you want to unload model '{_loadedModelId}'? (y/n)");
        var key = Console.ReadKey(true);
        Console.WriteLine(key.KeyChar);

        if (key.KeyChar == 'y' || key.KeyChar == 'Y')
        {
            try
            {
                await _depot!.UnloadModelAsync(_loadedModelId);
                Console.WriteLine($"Model '{_loadedModelId}' unloaded successfully");
                _loadedModelId = null;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to unload model: {ex.Message}", ex);
            }
        }
        else
        {
            Console.WriteLine("Model unload cancelled");
        }

        Console.WriteLine();
        Console.WriteLine("Press any key to continue...");
        Console.ReadKey(true);
    }

    // Utility method: Calculate cosine similarity
    private static double CalculateCosineSimilarity(float[] vec1, float[] vec2)
    {
        if (vec1.Length != vec2.Length)
            throw new ArgumentException("Vector dimensions must match");

        double dotProduct = 0.0;
        double magnitude1 = 0.0;
        double magnitude2 = 0.0;

        for (int i = 0; i < vec1.Length; i++)
        {
            dotProduct += vec1[i] * vec2[i];
            magnitude1 += vec1[i] * vec1[i];
            magnitude2 += vec2[i] * vec2[i];
        }

        magnitude1 = Math.Sqrt(magnitude1);
        magnitude2 = Math.Sqrt(magnitude2);

        if (magnitude1 == 0.0 || magnitude2 == 0.0)
            return 0.0;

        return dotProduct / (magnitude1 * magnitude2);
    }

    // Utility method: Format size
    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size:F2} {units[unitIndex]}";
    }
}