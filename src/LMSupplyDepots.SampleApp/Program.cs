using LMSupplyDepots.SDK;
using LMSupplyDepots.Contracts;
using LMSupplyDepots.ModelHub.Models;
using LMSupplyDepots.Models;
using System.Text.Json;
using System.Net.Http;
using System.Text;

namespace LMSupplyDepots.SampleApp;

public class Program
{
    private static LMSupplyDepot? _depot;
    private static readonly HttpClient _httpClient = new();

    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== LMSupplyDepot OpenAI-Compatible API Test ===\n");

        try
        {
            // Initialize LMSupplyDepot
            _depot = new LMSupplyDepot();
            // No InitializeAsync method needed, constructor initializes everything

            // Test model discovery and loading
            await TestModelDiscoveryAsync();

            // Start Host server for OpenAI-compatible API
            Console.WriteLine("\nStarting Host server for OpenAI-compatible API testing...");
            await StartHostServerAsync();

            // Test OpenAI-compatible endpoints
            await TestOpenAICompatibleAPIsAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
        finally
        {
            if (_depot != null)
            {
                _depot.Dispose(); // Use Dispose instead of DisposeAsync
            }
        }

        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }

    private static async Task TestModelDiscoveryAsync()
    {
        Console.WriteLine("=== Model Discovery and Loading ===");

        // Define target models
        var embeddingModelPath = @"D:\filer-data\models\nomic-ai_nomic-embed-text-v2-moe-GGUF\nomic-embed-text-v2-moe.Q2_K.gguf";
        var chatModelPath = @"D:\filer-data\models\unsloth_Llama-3.2-1B-Instruct-GGUF\Llama-3.2-1B-Instruct-UD-IQ1_S.gguf";

        // Check if local models exist
        if (!File.Exists(embeddingModelPath))
        {
            Console.WriteLine($"Local embedding model not found at: {embeddingModelPath}");
            Console.WriteLine("Attempting to download nomic-embed-text-v2-moe model...");
            await DownloadModelAsync("nomic-ai/nomic-embed-text-v2-moe-GGUF", "nomic-embed-text-v2-moe.Q2_K.gguf");
        }
        else
        {
            Console.WriteLine($"✓ Found local embedding model: {embeddingModelPath}");
        }

        if (!File.Exists(chatModelPath))
        {
            Console.WriteLine($"Local chat model not found at: {chatModelPath}");
            Console.WriteLine("Attempting to download Llama-3.2-1B-Instruct model...");
            await DownloadModelAsync("unsloth/Llama-3.2-1B-Instruct-GGUF", "Llama-3.2-1B-Instruct-UD-IQ1_S.gguf");
        }
        else
        {
            Console.WriteLine($"✓ Found local chat model: {chatModelPath}");
        }

        // List available models
        Console.WriteLine("\nListing available models...");
        var models = await _depot!.ListModelsAsync();
        foreach (var model in models)
        {
            var modelType = model.Capabilities.SupportsEmbeddings ? "Embedding" :
                           model.Capabilities.SupportsTextGeneration ? "TextGeneration" : "Unknown";
            Console.WriteLine($"- {model.Name} ({modelType}) - Path: {model.LocalPath ?? "Not downloaded"}");
        }

        // Test model loading for embedding
        await TestEmbeddingModelAsync();

        // Test model loading for chat
        await TestChatModelAsync();
    }

    private static async Task DownloadModelAsync(string repoId, string filename)
    {
        try
        {
            // Use the modelKey format expected by DownloadModelAsync
            var modelKey = $"{repoId}/{filename}";

            Console.WriteLine($"Downloading {modelKey}...");
            var result = await _depot!.DownloadModelAsync(modelKey);
            Console.WriteLine($"✓ Model downloaded successfully to: {result.LocalPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Failed to download model: {ex.Message}");
        }
    }

    private static async Task TestEmbeddingModelAsync()
    {
        Console.WriteLine("\n=== Testing Embedding Model ===");

        try
        {
            // Find embedding model
            var models = await _depot!.ListModelsAsync();
            var embeddingModel = models.FirstOrDefault(m =>
                m.Name.Contains("nomic-embed", StringComparison.OrdinalIgnoreCase) ||
                m.Capabilities.SupportsEmbeddings);

            if (embeddingModel == null)
            {
                Console.WriteLine("No embedding model found");
                return;
            }

            Console.WriteLine($"Loading embedding model: {embeddingModel.Name}");

            // Test embedding generation
            var texts = new[] { "Hello, world!", "This is a test sentence." };
            var embeddingResponse = await _depot.GenerateEmbeddingsAsync(embeddingModel.Key, texts);
            Console.WriteLine($"✓ Generated embeddings for {embeddingResponse.Embeddings.Count} texts");
            Console.WriteLine($"  Embedding dimension: {embeddingResponse.Dimension}");
            Console.WriteLine($"  Processing time: {embeddingResponse.ElapsedTime.TotalMilliseconds:F2}ms");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Embedding test failed: {ex.Message}");
        }
    }

    private static async Task TestChatModelAsync()
    {
        Console.WriteLine("\n=== Testing Chat Model ===");

        try
        {
            // Find chat model
            var models = await _depot!.ListModelsAsync();
            var chatModel = models.FirstOrDefault(m =>
                m.Name.Contains("llama", StringComparison.OrdinalIgnoreCase) ||
                m.Name.Contains("instruct", StringComparison.OrdinalIgnoreCase) ||
                m.Capabilities.SupportsTextGeneration);

            if (chatModel == null)
            {
                Console.WriteLine("No chat model found");
                return;
            }

            Console.WriteLine($"Loading chat model: {chatModel.Name}");

            // Test text generation
            var prompt = "Hello! How are you today?";
            var generationResponse = await _depot.GenerateTextAsync(chatModel.Key, prompt, maxTokens: 100, temperature: 0.7f);
            Console.WriteLine($"✓ Generated text:");
            Console.WriteLine($"  Prompt: {prompt}");
            Console.WriteLine($"  Response: {generationResponse.Text}");
            Console.WriteLine($"  Processing time: {generationResponse.ElapsedTime.TotalMilliseconds:F2}ms");
            Console.WriteLine($"  Tokens: {generationResponse.PromptTokens} prompt + {generationResponse.OutputTokens} output");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Chat test failed: {ex.Message}");
        }
    }
    private static async Task StartHostServerAsync()
    {
        // Note: This would typically start the Host server
        // For now, we assume it's running on localhost:5181
        Console.WriteLine("Assuming Host server is running on http://localhost:5181");

        // Wait a moment for any potential server startup
        await Task.Delay(2000);
    }

    private static async Task TestOpenAICompatibleAPIsAsync()
    {
        Console.WriteLine("\n=== Testing OpenAI-Compatible APIs ===");

        var baseUrl = "http://localhost:5181";

        // Test models endpoint
        await TestModelsAPIAsync(baseUrl);

        // Test chat completions endpoint
        await TestChatCompletionsAPIAsync(baseUrl);

        // Test embeddings endpoint
        await TestEmbeddingsAPIAsync(baseUrl);
    }

    private static async Task TestModelsAPIAsync(string baseUrl)
    {
        Console.WriteLine("\n--- Testing /v1/models endpoint ---");

        try
        {
            var response = await _httpClient.GetAsync($"{baseUrl}/v1/models");
            var responseContent = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"Status: {response.StatusCode}");
            Console.WriteLine($"Response: {responseContent}");

            if (response.IsSuccessStatusCode)
            {
                var modelsResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);
                Console.WriteLine("✓ Models API test successful");

                if (modelsResponse.TryGetProperty("object", out var objectType))
                {
                    Console.WriteLine($"  Object type: {objectType.GetString()}");
                }

                if (modelsResponse.TryGetProperty("data", out var data) && data.GetArrayLength() > 0)
                {
                    Console.WriteLine($"  Available models: {data.GetArrayLength()}");
                    foreach (var model in data.EnumerateArray())
                    {
                        if (model.TryGetProperty("id", out var modelId))
                        {
                            Console.WriteLine($"    - {modelId.GetString()}");
                        }
                    }
                }
            }
            else
            {
                Console.WriteLine("✗ Models API test failed");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Models API error: {ex.Message}");
        }
    }

    private static async Task TestChatCompletionsAPIAsync(string baseUrl)
    {
        Console.WriteLine("\n--- Testing /v1/chat/completions endpoint ---");

        try
        {
            var chatRequest = new
            {
                model = "llama-3-1b", // Use the alias from the API
                messages = new[]
                {
                    new { role = "system", content = "You are a helpful assistant." },
                    new { role = "user", content = "Hello! Can you tell me a short joke?" }
                },
                max_tokens = 100,
                temperature = 0.7
            };

            var json = JsonSerializer.Serialize(chatRequest, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            Console.WriteLine($"Request: {json}");

            var response = await _httpClient.PostAsync($"{baseUrl}/v1/chat/completions", content);
            var responseContent = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"Status: {response.StatusCode}");
            Console.WriteLine($"Response: {responseContent}");

            if (response.IsSuccessStatusCode)
            {
                var chatResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);
                Console.WriteLine("✓ Chat completions API test successful");

                if (chatResponse.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var firstChoice = choices[0];
                    if (firstChoice.TryGetProperty("message", out var message) &&
                        message.TryGetProperty("content", out var messageContent))
                    {
                        Console.WriteLine($"  Assistant response: {messageContent.GetString()}");
                    }
                }

                if (chatResponse.TryGetProperty("usage", out var usage))
                {
                    if (usage.TryGetProperty("prompt_tokens", out var promptTokens) &&
                        usage.TryGetProperty("completion_tokens", out var completionTokens) &&
                        usage.TryGetProperty("total_tokens", out var totalTokens))
                    {
                        Console.WriteLine($"  Usage: {promptTokens.GetInt32()} prompt + {completionTokens.GetInt32()} completion = {totalTokens.GetInt32()} total tokens");
                    }
                }
            }
            else
            {
                Console.WriteLine("✗ Chat completions API test failed");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Chat completions API error: {ex.Message}");
        }
    }

    private static async Task TestEmbeddingsAPIAsync(string baseUrl)
    {
        Console.WriteLine("\n--- Testing /v1/embeddings endpoint ---");

        try
        {
            var embeddingRequest = new
            {
                model = "hf:nomic-ai/nomic-embed-text-v2-moe-GGUF/nomic-embed-text-v2-moe.Q2_K", // Use the full model ID
                input = new[] { "Hello, world!", "This is a test." }
            };

            var json = JsonSerializer.Serialize(embeddingRequest, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            Console.WriteLine($"Request: {json}");

            var response = await _httpClient.PostAsync($"{baseUrl}/v1/embeddings", content);
            var responseContent = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"Status: {response.StatusCode}");
            Console.WriteLine($"Response: {responseContent}");

            if (response.IsSuccessStatusCode)
            {
                var embeddingResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);
                Console.WriteLine("✓ Embeddings API test successful");

                if (embeddingResponse.TryGetProperty("data", out var data) && data.GetArrayLength() > 0)
                {
                    Console.WriteLine($"  Generated {data.GetArrayLength()} embeddings");
                    var firstEmbedding = data[0];
                    if (firstEmbedding.TryGetProperty("embedding", out var embedding))
                    {
                        Console.WriteLine($"  First embedding dimension: {embedding.GetArrayLength()}");
                    }
                }

                if (embeddingResponse.TryGetProperty("usage", out var usage))
                {
                    if (usage.TryGetProperty("prompt_tokens", out var promptTokens) &&
                        usage.TryGetProperty("total_tokens", out var totalTokens))
                    {
                        Console.WriteLine($"  Usage: {promptTokens.GetInt32()} prompt tokens = {totalTokens.GetInt32()} total tokens");
                    }
                }

                if (embeddingResponse.TryGetProperty("model", out var modelUsed))
                {
                    Console.WriteLine($"  Model used: {modelUsed.GetString()}");
                }
            }
            else
            {
                Console.WriteLine("✗ Embeddings API test failed");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Embeddings API error: {ex.Message}");
        }
    }
}