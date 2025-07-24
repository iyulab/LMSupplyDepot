using LLama;
using LMSupplyDepots.External.LLamaEngine.Extensions;
using LMSupplyDepots.External.LLamaEngine.Services;
using LMSupplyDepots.SDK.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LMSupplyDepots.External.LLamaEngine.Examples;

/// <summary>
/// Example demonstrating dynamic GGUF metadata extraction and chat template application
/// </summary>
public class DynamicMetadataExample
{
    public static async Task RunExample()
    {
        // Setup dependency injection
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddLlamaModelMetadata();

        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<DynamicMetadataExample>>();
        var metadataService = serviceProvider.GetRequiredService<IModelMetadataService>();
        var llamaMetadataService = serviceProvider.GetRequiredService<LlamaModelMetadataService>();

        try
        {
            // Example 1: Load a model and extract metadata
            logger.LogInformation("=== Dynamic GGUF Metadata Extraction Example ===");

            // Load a model (this would be your actual model path)
            var modelPath = @"path\to\your\model.gguf";

            if (!File.Exists(modelPath))
            {
                logger.LogWarning("Model file not found at: {Path}. Using mock example.", modelPath);
                await RunMockExample(logger, metadataService);
                return;
            }

            // Initialize model
            var parameters = new ModelParams(modelPath)
            {
                ContextSize = 4096,
                GpuLayerCount = 0 // Use CPU for this example
            };

            using var model = LLamaWeights.LoadFromFile(parameters);
            using var modelHandle = new SafeLlamaModelHandle(model, parameters);

            // Register the model for metadata extraction
            llamaMetadataService.RegisterModelHandle("example-model", modelHandle);

            // Extract metadata
            var metadata = await metadataService.GetModelMetadataAsync("example-model");

            logger.LogInformation("Extracted Model Metadata:");
            logger.LogInformation("- Architecture: {Architecture}", metadata.Architecture);
            logger.LogInformation("- Model Name: {ModelName}", metadata.ModelName);
            logger.LogInformation("- Context Length: {ContextLength}", metadata.ContextLength);
            logger.LogInformation("- Supports Tool Calling: {SupportsToolCalling}", metadata.ToolCapabilities.SupportsToolCalling);
            logger.LogInformation("- Tool Call Format: {ToolCallFormat}", metadata.ToolCapabilities.ToolCallFormat);
            logger.LogInformation("- Chat Template Available: {HasTemplate}", !string.IsNullOrEmpty(metadata.ChatTemplate));

            // Example 2: Apply dynamic chat template
            logger.LogInformation("\n=== Dynamic Chat Template Application ===");

            var messages = new List<ChatMessage>
            {
                new() { Role = "system", Content = "You are a helpful assistant." },
                new() { Role = "user", Content = "What's the weather like today?" }
            };

            // Apply chat template based on model's native format
            var formattedPrompt = await metadataService.ApplyChatTemplateAsync(
                "example-model",
                messages,
                addGenerationPrompt: true);

            logger.LogInformation("Formatted prompt using model's native template:");
            logger.LogInformation("{FormattedPrompt}", formattedPrompt);

            // Example 3: Tool calling with dynamic format
            if (metadata.ToolCapabilities.SupportsToolCalling)
            {
                logger.LogInformation("\n=== Dynamic Tool Calling Format ===");

                var toolOptions = new ToolCallOptions
                {
                    Tools = new[]
                    {
                        new ToolDefinition
                        {
                            Name = "get_weather",
                            Description = "Get current weather information",
                            Parameters = new { location = new { type = "string", description = "City name" } }
                        }
                    }
                };

                var toolFormattedPrompt = await metadataService.ApplyChatTemplateAsync(
                    "example-model",
                    messages,
                    addGenerationPrompt: true,
                    toolOptions);

                logger.LogInformation("Tool-enabled prompt using model's native format:");
                logger.LogInformation("{ToolFormattedPrompt}", toolFormattedPrompt);
            }

            // Cleanup
            llamaMetadataService.UnregisterModelHandle("example-model");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error running dynamic metadata example");
        }
    }

    /// <summary>
    /// Run a mock example when no actual model file is available
    /// </summary>
    private static async Task RunMockExample(ILogger logger, IModelMetadataService metadataService)
    {
        logger.LogInformation("Running mock example to demonstrate the concept...");

        // This would show how the system works conceptually
        logger.LogInformation("\nConceptual Example:");
        logger.LogInformation("1. Load GGUF model -> Extract metadata automatically");
        logger.LogInformation("2. Model metadata includes:");
        logger.LogInformation("   - Architecture (phi3, llama, mixtral, etc.)");
        logger.LogInformation("   - Chat template (Jinja2 format)");
        logger.LogInformation("   - Special tokens (BOS, EOS, tool tokens)");
        logger.LogInformation("   - Tool calling capabilities");
        logger.LogInformation("3. Apply model-specific formatting dynamically");
        logger.LogInformation("4. No more hardcoded tool prompt formats!");

        logger.LogInformation("\nExample tool formatting for different architectures:");
        logger.LogInformation("Phi-4-mini: <|tool|>{{json}}<|/tool|>");
        logger.LogInformation("Llama: [TOOL_CALL] function_name(args) [/TOOL_CALL]");
        logger.LogInformation("Mixtral: {{\"tool_call\": {{\"name\": \"func\", \"args\": {{}}}}}}");
        logger.LogInformation("Generic: TOOL_CALL: function_name(args)");
    }
}

/// <summary>
/// Example showing integration with LMSupplyDepot SDK
/// </summary>
public class SdkIntegrationExample
{
    public static void ShowSdkIntegration()
    {
        var code = @"
// In your LMSupplyDepot setup:
var services = new ServiceCollection();

// Add LMSupplyDepot services
services.AddLMSupplyDepot();

// Add LLamaEngine metadata services
services.AddLlamaModelMetadata();

var serviceProvider = services.BuildServiceProvider();
var depot = serviceProvider.GetRequiredService<LMSupplyDepot>();

// Load a model
await depot.LoadModelAsync(""model-id"", ""path/to/model.gguf"");

// Now OpenAI-compatible requests automatically use the model's native format!
var response = await depot.CreateChatCompletionAsync(new OpenAIChatCompletionRequest
{
    Model = ""model-id"",
    Messages = new[]
    {
        new OpenAIChatMessage { Role = ""user"", Content = ""Hello!"" }
    },
    Tools = new[] 
    {
        new Tool { Function = new Function { Name = ""get_weather"", ... } }
    }
});

// The tool formatting is automatically applied based on the model's extracted metadata!
// No more hardcoded <|tool|> tags or manual prompt engineering.
";

        Console.WriteLine("SDK Integration Example:");
        Console.WriteLine(code);
    }
}

/// <summary>
/// Performance comparison showing the benefits
/// </summary>
public class PerformanceComparison
{
    public static void ShowComparison()
    {
        Console.WriteLine("=== Before vs After Comparison ===");
        Console.WriteLine();

        Console.WriteLine("BEFORE (Hardcoded approach):");
        Console.WriteLine("❌ Only works with Phi-4-mini");
        Console.WriteLine("❌ Hardcoded <|tool|> format");
        Console.WriteLine("❌ Manual prompt engineering for each model");
        Console.WriteLine("❌ Breaks when using different model architectures");
        Console.WriteLine("❌ No automatic chat template application");
        Console.WriteLine();

        Console.WriteLine("AFTER (Dynamic GGUF metadata approach):");
        Console.WriteLine("✅ Works with any GGUF model");
        Console.WriteLine("✅ Automatically detects tool calling format");
        Console.WriteLine("✅ Uses model's native chat template");
        Console.WriteLine("✅ Extracts special tokens dynamically");
        Console.WriteLine("✅ Architecture-agnostic implementation");
        Console.WriteLine("✅ Zero manual configuration needed");
        Console.WriteLine("✅ Future-proof for new model formats");
    }
}
