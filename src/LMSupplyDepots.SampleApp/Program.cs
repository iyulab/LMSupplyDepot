using LMSupplyDepots.SDK;
using LMSupplyDepots.SDK.OpenAI.Models;
using Microsoft.Extensions.Logging;

Console.WriteLine("🚀 LMSupplyDepots Simple Text Generation Test");
Console.WriteLine("============================================\n");

// Configure logging
using var loggerFactory = LoggerFactory.Create(builder =>
    builder.AddConsole().SetMinimumLevel(LogLevel.Debug));

// Configure options
var options = new LMSupplyDepotOptions
{
    DataPath = @"D:\filer-data"
};

// Create depot
var depot = new LMSupplyDepot(options, loggerFactory);

try
{
    // Load model
    // Test with different model types to verify dynamic architecture detection
    var modelKey = args.Length > 0 ? args[0] : "hf:bartowski/microsoft_Phi-4-mini-instruct-GGUF/microsoft_Phi-4-mini-instruct-Q8_0";

    Console.WriteLine($"📥 Loading model: {modelKey}");

    await depot.LoadModelAsync(modelKey);
    Console.WriteLine("✅ Model loaded!\n");

    // Test 1: Simple text generation without tools
    Console.WriteLine("🤖 Test 1: Simple Text Generation");
    Console.WriteLine("==================================");

    var simpleRequest = new OpenAIChatCompletionRequest
    {
        Model = modelKey,
        Messages = new List<OpenAIChatMessage>
        {
            new()
            {
                Role = "user",
                Content = new TextContentPart { Text = "Tell me a short joke about programming." }
            }
        },
        MaxCompletionTokens = 100,
        Temperature = 1.0f,
        TopP = 1.0f,
        FrequencyPenalty = 0.0f,
        PresencePenalty = 0.0f
        // No stop sequences to prevent early termination
    };

    Console.WriteLine("👤 User: Tell me a short joke about programming.");

    var simpleResponse = await depot.CreateChatCompletionAsync(simpleRequest);
    var simpleMessage = simpleResponse.Choices[0].Message;

    Console.WriteLine($"🔍 Response details: Choices count: {simpleResponse.Choices.Count}");
    Console.WriteLine($"🔍 Message content type: {simpleMessage.Content?.GetType().Name}");

    if (simpleMessage.Content is TextContentPart simpleContent)
    {
        Console.WriteLine($"🤖 Assistant: {simpleContent.Text}\n");
        Console.WriteLine($"🔍 Text length: {simpleContent.Text?.Length ?? 0}");
    }
    else
    {
        Console.WriteLine($"🤖 Assistant: (No text content - type: {simpleMessage.Content?.GetType().Name})\n");
    }

    // Test 2: Tool-enabled text generation
    Console.WriteLine("🔧 Test 2: Tool-Enabled Generation");
    Console.WriteLine("===================================");

    var toolRequest = new OpenAIChatCompletionRequest
    {
        Model = modelKey,
        Messages = new List<OpenAIChatMessage>
        {
            new()
            {
                Role = "user",
                Content = new TextContentPart { Text = "현재 시간을 알려주세요." }
            }
        },
        Tools = new List<Tool>
        {
            new()
            {
                Type = "function",
                Function = new FunctionDefinition
                {
                    Name = "get_time",
                    Description = "현재 시간을 가져옵니다",
                    Parameters = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object>()
                    }
                }
            }
        },
        MaxCompletionTokens = 256, // Sufficient tokens for tool calling
        Temperature = 0.6f, // Moderate temperature for tool usage
        TopP = 0.9f,
        FrequencyPenalty = 0.0f,
        PresencePenalty = 0.0f
    };

    Console.WriteLine("👤 User: 현재 시간을 알려주세요.");

    var toolResponse = await depot.CreateChatCompletionAsync(toolRequest);
    var toolMessage = toolResponse.Choices[0].Message;

    Console.WriteLine($"🔍 Tool response details: Choices count: {toolResponse.Choices.Count}");
    Console.WriteLine($"🔍 Tool message content type: {toolMessage.Content?.GetType().Name}");

    if (toolMessage.Content is TextContentPart toolContent)
    {
        Console.WriteLine($"🤖 Assistant (with tools): {toolContent.Text}");
        Console.WriteLine($"🔍 Tool text length: {toolContent.Text?.Length ?? 0}");
    }
    else
    {
        Console.WriteLine($"🤖 Assistant (with tools): (No text content - type: {toolMessage.Content?.GetType().Name})");
    }

    if (toolMessage.ToolCalls?.Count > 0)
    {
        Console.WriteLine($"🔧 Tool calls requested: {toolMessage.ToolCalls.Count}");
        foreach (var toolCall in toolMessage.ToolCalls)
        {
            Console.WriteLine($"   - {toolCall.Function?.Name}: {toolCall.Function?.Arguments}");
        }
    }
    else
    {
        Console.WriteLine("ℹ️  No tool calls detected in response");
    }

    Console.WriteLine("\n✅ Both tests completed successfully!");
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Error: {ex.Message}");
    if (ex.InnerException != null)
    {
        Console.WriteLine($"   Inner: {ex.InnerException.Message}");
    }
}

Console.WriteLine("\n🎯 Tests completed.");
