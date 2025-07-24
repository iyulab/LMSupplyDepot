//using LMSupplyDepots.SDK;
//using LMSupplyDepots.SDK.OpenAI;
//using LMSupplyDepots.SDK.OpenAI.Models;
//using LMSupplyDepots.SDK.OpenAI.Services;
//using LMSupplyDepots.SDK.Tools;
//using LMSupplyDepots.SDK.Extensions;
//using LMSupplyDepots.Contracts;
//using Microsoft.Extensions.Logging;
//using Microsoft.Extensions.DependencyInjection;
//using System.Diagnostics;
//using System.Text.Json;

//namespace LMSupplyDepots.SampleApp;

///// <summary>
///// Sample application demonstrating SDK functionality
///// </summary>
//public class Program
//{
//    private static LMSupplyDepot? _depot;

//    public static async Task Main(string[] args)
//    {
//        Console.WriteLine("=== LMSupplyDepots SDK Sample Application ===");
//        Console.WriteLine("Testing OpenAI compatibility and tools functionality in SDK\n");

//        try
//        {
//            // Initialize the SDK with OpenAI and tools support
//            await InitializeSDKAsync();

//            // Test core SDK functionality
//            await TestSDKFunctionalityAsync();

//            // Test OpenAI compatibility
//            await TestOpenAICompatibilityAsync();

//            // Test tools functionality
//            await TestToolsFunctionalityAsync();

//            Console.WriteLine("\n=== All tests completed ===");
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"Application error: {ex.Message}");
//            Console.WriteLine($"Stack trace: {ex.StackTrace}");
//        }
//        finally
//        {
//            // Cleanup
//            if (_depot != null)
//            {
//                try
//                {
//                    _depot.Dispose();
//                    Console.WriteLine("SDK disposed successfully");
//                }
//                catch (Exception ex)
//                {
//                    Console.WriteLine($"Error during SDK disposal: {ex.Message}");
//                }
//            }
//        }

//        Console.WriteLine("Press any key to exit...");
//        Console.ReadKey();
//    }

//    /// <summary>
//    /// Initializes the SDK with proper configuration
//    /// </summary>
//    private static Task InitializeSDKAsync()
//    {
//        Console.WriteLine("--- SDK Initialization ---");

//        try
//        {
//            var options = new LMSupplyDepotOptions
//            {
//                DataPath = @"D:\filer-data",
//                EnableModelCaching = true,
//                MaxCachedModels = 2,
//                DefaultTimeoutMs = 30000,
//                LLamaOptions = new LMSupplyDepots.SDK.LLamaOptions
//                {
//                    GpuLayers = 35,
//                    ContextSize = 4096,
//                    BatchSize = 512
//                }
//            };

//            _depot = new LMSupplyDepot(options);

//            // Register built-in tools
//            var weatherTool = new GetWeatherTool();
//            var calculatorTool = new CalculatorTool();

//            _depot.RegisterTool(weatherTool);
//            _depot.RegisterTool(calculatorTool);

//            Console.WriteLine("✅ SDK with OpenAI and tools support initialized successfully");
//            return Task.CompletedTask;
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"❌ SDK initialization failed: {ex.Message}");
//            throw;
//        }
//    }

//    /// <summary>
//    /// Tests all core SDK functionality
//    /// </summary>
//    private static async Task TestSDKFunctionalityAsync()
//    {
//        Console.WriteLine("\n=== Core SDK Functionality Tests ===");

//        // Test 1: Model discovery and management
//        await TestModelManagement();

//        // Test 2: Model loading and unloading
//        await TestModelLoading();

//        // Test 3: Text generation capabilities
//        await TestTextGeneration();

//        // Test 4: Streaming text generation
//        await TestStreamingGeneration();

//        // Test 5: Text generation with different parameters
//        await TestParameterizedGeneration();

//        // Test 6: Embedding generation (if supported)
//        await TestEmbeddingGeneration();

//        // Test 7: Concurrent operations
//        await TestConcurrentOperations();

//        // Test 8: Error handling and recovery
//        await TestErrorHandling();
//    }

//    /// <summary>
//    /// Tests model discovery and management capabilities
//    /// </summary>
//    private static async Task TestModelManagement()
//    {
//        Console.WriteLine("\n--- Model Management Tests ---");

//        try
//        {
//            // List all available models
//            var models = await _depot!.ListModelsAsync();
//            Console.WriteLine($"✅ Found {models.Count} models");

//            foreach (var model in models.Take(5)) // Show first 5 models
//            {
//                Console.WriteLine($"  - {model.Name} ({model.Id})");
//                Console.WriteLine($"    Type: {model.Type}, Format: {model.Format}");
//                Console.WriteLine($"    Size: {model.SizeInBytes / (1024.0 * 1024 * 1024):F2} GB, Loaded: {model.IsLoaded}");
//                Console.WriteLine($"    Capabilities: Text={model.Capabilities.SupportsTextGeneration}, Embeddings={model.Capabilities.SupportsEmbeddings}");
//            }

//            if (models.Count > 5)
//            {
//                Console.WriteLine($"  ... and {models.Count - 5} more models");
//            }

//            // Test model search by name pattern - remove this since SearchModelsAsync doesn't exist
//            // var searchResults = await _depot.SearchModelsAsync("nano");
//            // Console.WriteLine($"✅ Search for 'nano' found {searchResults.Count} models");
//            Console.WriteLine($"✅ Model discovery completed");
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"❌ Model management test failed: {ex.Message}");
//        }
//    }

//    /// <summary>
//    /// Tests model loading and unloading
//    /// </summary>
//    private static async Task TestModelLoading()
//    {
//        Console.WriteLine("\n--- Model Loading Tests ---");

//        try
//        {
//            var models = await _depot!.ListModelsAsync();
//            var testModel = models.FirstOrDefault(m => m.Capabilities.SupportsTextGeneration);

//            if (testModel == null)
//            {
//                Console.WriteLine("❌ No text generation model found");
//                return;
//            }

//            Console.WriteLine($"Testing with model: {testModel.Name}");

//            // Load the model
//            if (!testModel.IsLoaded)
//            {
//                Console.WriteLine("Loading model...");
//                var stopwatch = Stopwatch.StartNew();
//                await _depot.LoadModelAsync(testModel.Id);
//                stopwatch.Stop();
//                Console.WriteLine($"✅ Model loaded in {stopwatch.ElapsedMilliseconds}ms");
//            }
//            else
//            {
//                Console.WriteLine("✅ Model already loaded");
//            }

//            // Verify model is loaded
//            var loadedModel = await _depot.GetModelAsync(testModel.Id);
//            if (loadedModel?.IsLoaded == true)
//            {
//                Console.WriteLine("✅ Model load status verified");
//            }
//            else
//            {
//                Console.WriteLine("❌ Model load status verification failed");
//            }
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"❌ Model loading test failed: {ex.Message}");
//        }
//    }

//    /// <summary>
//    /// Tests basic text generation
//    /// </summary>
//    private static async Task TestTextGeneration()
//    {
//        Console.WriteLine("\n--- Text Generation Tests ---");

//        try
//        {
//            var models = await _depot!.ListModelsAsync();
//            var testModel = models.FirstOrDefault(m => m.IsLoaded && m.Capabilities.SupportsTextGeneration);

//            if (testModel == null)
//            {
//                Console.WriteLine("❌ No loaded text generation model found");
//                return;
//            }

//            Console.WriteLine($"Testing with model: {testModel.Name}");

//            // Test 1: Simple text generation
//            var prompt = "What is artificial intelligence?";
//            Console.WriteLine($"Prompt: {prompt}");

//            var stopwatch = Stopwatch.StartNew();
//            var response = await _depot.GenerateTextAsync(
//                testModel.Id,
//                prompt,
//                maxTokens: 100,
//                temperature: 0.7f);
//            stopwatch.Stop();

//            Console.WriteLine($"✅ Generation completed in {stopwatch.ElapsedMilliseconds}ms");
//            Console.WriteLine($"Response: {response.Text}");
//            Console.WriteLine($"Output tokens: {response.OutputTokens}");
//            Console.WriteLine($"Generation speed: {response.OutputTokens / (stopwatch.ElapsedMilliseconds / 1000.0):F2} tokens/sec");
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"❌ Text generation test failed: {ex.Message}");
//        }
//    }

//    /// <summary>
//    /// Tests streaming text generation
//    /// </summary>
//    private static async Task TestStreamingGeneration()
//    {
//        Console.WriteLine("\n--- Streaming Generation Tests ---");

//        try
//        {
//            var models = await _depot!.ListModelsAsync();
//            var testModel = models.FirstOrDefault(m => m.IsLoaded && m.Capabilities.SupportsTextGeneration);

//            if (testModel == null)
//            {
//                Console.WriteLine("❌ No loaded text generation model found");
//                return;
//            }

//            Console.WriteLine($"Testing streaming with model: {testModel.Name}");

//            var prompt = "Explain the benefits of renewable energy in 3 points:";
//            Console.WriteLine($"Prompt: {prompt}");
//            Console.Write("Response: ");

//            var stopwatch = Stopwatch.StartNew();
//            var tokenCount = 0;

//            await foreach (var token in _depot.GenerateTextStreamAsync(
//                testModel.Id,
//                prompt,
//                maxTokens: 150,
//                temperature: 0.8f))
//            {
//                Console.Write(token);
//                tokenCount++;
//            }
//            stopwatch.Stop();

//            Console.WriteLine();
//            Console.WriteLine($"✅ Streaming completed in {stopwatch.ElapsedMilliseconds}ms");
//            Console.WriteLine($"Tokens streamed: {tokenCount}");
//            Console.WriteLine($"Streaming speed: {tokenCount / (stopwatch.ElapsedMilliseconds / 1000.0):F2} tokens/sec");
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"❌ Streaming generation test failed: {ex.Message}");
//        }
//    }

//    /// <summary>
//    /// Tests text generation with different parameters
//    /// </summary>
//    private static async Task TestParameterizedGeneration()
//    {
//        Console.WriteLine("\n--- Parameterized Generation Tests ---");

//        try
//        {
//            var models = await _depot!.ListModelsAsync();
//            var testModel = models.FirstOrDefault(m => m.IsLoaded && m.Capabilities.SupportsTextGeneration);

//            if (testModel == null)
//            {
//                Console.WriteLine("❌ No loaded text generation model found");
//                return;
//            }

//            var prompt = "Write a short creative story:";

//            // Test with different temperature values
//            var temperatures = new[] { 0.1f, 0.7f, 1.2f };

//            foreach (var temp in temperatures)
//            {
//                Console.WriteLine($"\nTesting with temperature: {temp}");

//                var response = await _depot.GenerateTextAsync(
//                    testModel.Id,
//                    prompt,
//                    maxTokens: 80,
//                    temperature: temp,
//                    topP: 0.9f);

//                Console.WriteLine($"Response: {response.Text.Substring(0, Math.Min(100, response.Text.Length))}...");
//                Console.WriteLine($"Tokens: {response.OutputTokens}, Time: {response.ElapsedTime.TotalMilliseconds:F0}ms");
//            }

//            Console.WriteLine("✅ Parameterized generation tests completed");
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"❌ Parameterized generation test failed: {ex.Message}");
//        }
//    }

//    /// <summary>
//    /// Tests embedding generation if supported
//    /// </summary>
//    private static async Task TestEmbeddingGeneration()
//    {
//        Console.WriteLine("\n--- Embedding Generation Tests ---");

//        try
//        {
//            var models = await _depot!.ListModelsAsync();
//            var embeddingModel = models.FirstOrDefault(m => m.Capabilities.SupportsEmbeddings);

//            if (embeddingModel == null)
//            {
//                Console.WriteLine("ℹ️ No embedding model available, skipping embedding tests");
//                return;
//            }

//            Console.WriteLine($"Testing with model: {embeddingModel.Name}");

//            var texts = new[]
//            {
//                "Hello world",
//                "Artificial intelligence is transforming technology",
//                "The weather is nice today"
//            };

//            var stopwatch = Stopwatch.StartNew();
//            var response = await _depot.GenerateEmbeddingsAsync(embeddingModel.Id, texts);
//            stopwatch.Stop();

//            Console.WriteLine($"✅ Embedding generation successful in {stopwatch.ElapsedMilliseconds}ms");
//            Console.WriteLine($"Processed {response.Embeddings.Count} texts");
//            Console.WriteLine($"Embedding dimension: {response.Dimension}");
//            Console.WriteLine($"Average processing time per text: {stopwatch.ElapsedMilliseconds / (double)texts.Length:F2}ms");
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"❌ Embedding generation test failed: {ex.Message}");
//        }
//    }

//    /// <summary>
//    /// Tests concurrent operations
//    /// </summary>
//    private static async Task TestConcurrentOperations()
//    {
//        Console.WriteLine("\n--- Concurrent Operations Tests ---");

//        try
//        {
//            var models = await _depot!.ListModelsAsync();
//            var testModel = models.FirstOrDefault(m => m.IsLoaded && m.Capabilities.SupportsTextGeneration);

//            if (testModel == null)
//            {
//                Console.WriteLine("❌ No loaded text generation model found");
//                return;
//            }

//            Console.WriteLine($"Testing concurrent operations with model: {testModel.Name}");

//            var prompts = new[]
//            {
//                "What is machine learning?",
//                "Explain quantum computing.",
//                "Describe blockchain technology.",
//                "What are neural networks?"
//            };

//            var stopwatch = Stopwatch.StartNew();
//            var tasks = prompts.Select(async prompt =>
//            {
//                var response = await _depot.GenerateTextAsync(
//                    testModel.Id,
//                    prompt,
//                    maxTokens: 50,
//                    temperature: 0.7f);
//                return new { Prompt = prompt, Response = response };
//            });

//            var results = await Task.WhenAll(tasks);
//            stopwatch.Stop();

//            Console.WriteLine($"✅ {results.Length} concurrent generations completed in {stopwatch.ElapsedMilliseconds}ms");

//            foreach (var result in results)
//            {
//                Console.WriteLine($"  Q: {result.Prompt}");
//                Console.WriteLine($"  A: {result.Response.Text.Substring(0, Math.Min(60, result.Response.Text.Length))}...");
//                Console.WriteLine($"     ({result.Response.OutputTokens} tokens)");
//            }
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"❌ Concurrent operations test failed: {ex.Message}");
//        }
//    }

//    /// <summary>
//    /// Tests error handling and recovery
//    /// </summary>
//    private static async Task TestErrorHandling()
//    {
//        Console.WriteLine("\n--- Error Handling Tests ---");

//        try
//        {
//            // Test 1: Invalid model key
//            Console.WriteLine("Testing invalid model key...");
//            try
//            {
//                await _depot!.GenerateTextAsync("invalid-model-id", "Test prompt");
//                Console.WriteLine("❌ Expected error for invalid model key");
//            }
//            catch (Exception ex)
//            {
//                Console.WriteLine($"✅ Correctly handled invalid model key: {ex.GetType().Name}");
//            }

//            // Test 2: Empty prompt
//            Console.WriteLine("Testing empty prompt...");
//            var models = await _depot!.ListModelsAsync();
//            var testModel = models.FirstOrDefault(m => m.IsLoaded && m.Capabilities.SupportsTextGeneration);

//            if (testModel != null)
//            {
//                try
//                {
//                    await _depot.GenerateTextAsync(testModel.Id, "");
//                    Console.WriteLine("✅ Empty prompt handled gracefully");
//                }
//                catch (Exception ex)
//                {
//                    Console.WriteLine($"✅ Empty prompt error handled: {ex.GetType().Name}");
//                }
//            }

//            // Test 3: Excessive token request
//            Console.WriteLine("Testing excessive token request...");
//            if (testModel != null)
//            {
//                try
//                {
//                    await _depot.GenerateTextAsync(testModel.Id, "Test", maxTokens: 100000);
//                    Console.WriteLine("✅ Excessive token request handled");
//                }
//                catch (Exception ex)
//                {
//                    Console.WriteLine($"✅ Excessive token error handled: {ex.GetType().Name}");
//                }
//            }

//            Console.WriteLine("✅ Error handling tests completed");
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"❌ Error handling test failed: {ex.Message}");
//        }
//    }

//    /// <summary>
//    /// Tests OpenAI compatibility functionality
//    /// </summary>
//    private static async Task TestOpenAICompatibilityAsync()
//    {
//        Console.WriteLine("\n--- OpenAI Compatibility Tests ---");

//        try
//        {
//            // Test 1: List models in OpenAI format
//            Console.WriteLine("Testing OpenAI models listing...");
//            var openAIModels = await _depot!.ListModelsOpenAIAsync();
//            Console.WriteLine($"✅ Found {openAIModels.Data.Count} models in OpenAI format");

//            foreach (var model in openAIModels.Data.Take(3))
//            {
//                Console.WriteLine($"  - {model.Id} ({model.Type}, owned by {model.OwnedBy})");
//            }

//            // Test 2: Chat completion with OpenAI format
//            Console.WriteLine("\nTesting OpenAI chat completion...");
//            var loadedModel = (await _depot.ListModelsAsync())
//                .FirstOrDefault(m => m.IsLoaded && m.Capabilities.SupportsTextGeneration);

//            if (loadedModel != null)
//            {
//                var chatRequest = new OpenAIChatCompletionRequest
//                {
//                    Model = loadedModel.Key,
//                    Messages = new List<OpenAIChatMessage>
//                    {
//                        new() { Role = "system", Content = "You are a helpful assistant." },
//                        new() { Role = "user", Content = "What is the capital of France?" }
//                    },
//                    MaxCompletionTokens = 50,
//                    Temperature = 0.7f
//                };

//                var chatResponse = await _depot.CreateChatCompletionAsync(chatRequest);
//                Console.WriteLine($"✅ Chat completion successful");
//                Console.WriteLine($"  Response ID: {chatResponse.Id}");
//                Console.WriteLine($"  Model: {chatResponse.Model}");
//                Console.WriteLine($"  Usage: {chatResponse.Usage.PromptTokens} prompt + {chatResponse.Usage.CompletionTokens} completion = {chatResponse.Usage.TotalTokens} total tokens");

//                if (chatResponse.Choices.Count > 0)
//                {
//                    var content = chatResponse.Choices[0].Message.Content;
//                    var text = content is TextContentPart textPart ? textPart.Text : content?.ToString() ?? "";
//                    Console.WriteLine($"  Assistant: {text.Trim()}");
//                }

//                // Test 3: Streaming chat completion
//                Console.WriteLine("\nTesting OpenAI streaming chat completion...");
//                chatRequest.Stream = true;
//                var streamChunks = new List<string>();

//                await foreach (var chunk in _depot.CreateChatCompletionStreamAsync(chatRequest))
//                {
//                    streamChunks.Add(chunk);
//                    if (streamChunks.Count >= 5) break; // Limit for demo
//                }

//                Console.WriteLine($"✅ Streaming completion successful, received {streamChunks.Count} chunks");
//                if (streamChunks.Count > 0)
//                {
//                    Console.WriteLine($"  First chunk: {streamChunks[0]}");
//                }
//            }
//            else
//            {
//                Console.WriteLine("⚠️ No loaded text generation model found for OpenAI testing");
//            }

//            Console.WriteLine("✅ OpenAI compatibility tests completed");
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"❌ OpenAI compatibility test failed: {ex.Message}");
//            Console.WriteLine($"Stack trace: {ex.StackTrace}");
//        }
//    }

//    /// <summary>
//    /// Tests tools functionality
//    /// </summary>
//    private static async Task TestToolsFunctionalityAsync()
//    {
//        Console.WriteLine("\n--- Tools Functionality Tests ---");

//        try
//        {
//            // Test 1: List available tools
//            Console.WriteLine("Testing tools listing...");
//            var availableTools = _depot!.GetAvailableTools();
//            Console.WriteLine($"✅ Found {availableTools.Count} available tools");

//            foreach (var tool in availableTools)
//            {
//                Console.WriteLine($"  - {tool.Function.Name}: {tool.Function.Description}");
//            }

//            // Test 2: Execute weather tool
//            Console.WriteLine("\nTesting weather tool execution...");
//            var weatherArgs = JsonSerializer.Serialize(new { location = "Seoul, South Korea", unit = "celsius" });

//            if (_depot.IsToolAvailable("get_weather"))
//            {
//                var weatherResult = await _depot.ExecuteToolAsync("get_weather", weatherArgs);
//                Console.WriteLine($"✅ Weather tool executed successfully");
//                Console.WriteLine($"  Result: {weatherResult}");
//            }
//            else
//            {
//                Console.WriteLine("⚠️ Weather tool not available");
//            }

//            // Test 3: Execute calculator tool
//            Console.WriteLine("\nTesting calculator tool execution...");
//            var calcArgs = JsonSerializer.Serialize(new { expression = "25 + 17" });

//            if (_depot.IsToolAvailable("calculate"))
//            {
//                var calcResult = await _depot.ExecuteToolAsync("calculate", calcArgs);
//                Console.WriteLine($"✅ Calculator tool executed successfully");
//                Console.WriteLine($"  Result: {calcResult}");
//            }
//            else
//            {
//                Console.WriteLine("⚠️ Calculator tool not available");
//            }

//            // Test 4: Chat completion with tools
//            Console.WriteLine("\nTesting chat completion with tools...");
//            var loadedModel = (await _depot.ListModelsAsync())
//                .FirstOrDefault(m => m.IsLoaded && m.Capabilities.SupportsTextGeneration);

//            if (loadedModel != null)
//            {
//                var chatWithToolsRequest = new OpenAIChatCompletionRequest
//                {
//                    Model = loadedModel.Key,
//                    Messages = new List<OpenAIChatMessage>
//                    {
//                        new() { Role = "system", Content = "You are a helpful assistant with access to tools. Use the weather tool to get weather information and calculator tool for math." },
//                        new() { Role = "user", Content = "What's the weather like in Tokyo, Japan? Also, what's 15 multiplied by 23?" }
//                    },
//                    Tools = availableTools,
//                    MaxCompletionTokens = 150,
//                    Temperature = 0.7f
//                };

//                var toolChatResponse = await _depot.CreateChatCompletionAsync(chatWithToolsRequest);
//                Console.WriteLine($"✅ Chat with tools completed");
//                Console.WriteLine($"  Model understood tools are available: {(toolChatResponse.Choices[0].Message.ToolCalls?.Count > 0 ? "Yes" : "No")}");

//                if (toolChatResponse.Choices.Count > 0)
//                {
//                    var content = toolChatResponse.Choices[0].Message.Content;
//                    var text = content is TextContentPart textPart ? textPart.Text : content?.ToString() ?? "";
//                    Console.WriteLine($"  Assistant: {text.Trim()}");
//                }
//            }
//            else
//            {
//                Console.WriteLine("⚠️ No loaded text generation model found for tools testing");
//            }

//            Console.WriteLine("✅ Tools functionality tests completed");
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"❌ Tools functionality test failed: {ex.Message}");
//            Console.WriteLine($"Stack trace: {ex.StackTrace}");
//        }
//    }
//}
