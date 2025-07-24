using LMSupplyDepots.SDK;
using LMSupplyDepots.SDK.OpenAI.Models;
using Microsoft.Extensions.Logging;

Console.WriteLine("🚀 LMSupplyDepots Tool Workflow Demo");
Console.WriteLine("=====================================\n");

// Configure logging
using var loggerFactory = LoggerFactory.Create(builder =>
    builder.AddConsole().SetMinimumLevel(LogLevel.Information));

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
    var modelKey = "hf:bartowski/microsoft_Phi-4-mini-instruct-GGUF/microsoft_Phi-4-mini-instruct-Q8_0";
    Console.WriteLine($"📥 Loading model: {modelKey}");

    await depot.LoadModelAsync(modelKey);
    Console.WriteLine("✅ Model loaded!\n");

    // Sample tool functions
    Console.WriteLine("🔧 Sample Tool Functions Available:");
    Console.WriteLine("- get_weather: Get current weather for a location");
    Console.WriteLine("- calculate: Perform mathematical calculations");
    Console.WriteLine("- get_time: Get current time\n");

    // User request
    var userQuestion = "What's the weather like in Seoul and what time is it now?";
    Console.WriteLine($"👤 User: {userQuestion}\n");

    // Step 1: Initial request with tools
    Console.WriteLine("🤖 Step 1: Asking AI assistant with available tools...");
    var request = new OpenAIChatCompletionRequest
    {
        Model = modelKey,
        Messages = new List<OpenAIChatMessage>
        {
            new()
            {
                Role = "user",
                Content = new TextContentPart { Text = userQuestion }
            }
        },
        Tools = new List<Tool>
        {
            new()
            {
                Type = "function",
                Function = new FunctionDefinition
                {
                    Name = "get_weather",
                    Description = "Get the current weather for a specific location",
                    Parameters = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object>
                        {
                            ["location"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] = "The location to get weather for"
                            }
                        },
                        ["required"] = new[] { "location" }
                    }
                }
            },
            new()
            {
                Type = "function",
                Function = new FunctionDefinition
                {
                    Name = "get_time",
                    Description = "Get the current time",
                    Parameters = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object>()
                    }
                }
            }
        },
        MaxCompletionTokens = 200,
        Temperature = 0.1f
    };

    var response = await depot.CreateChatCompletionAsync(request);
    var assistantMessage = response.Choices[0].Message;

    Console.WriteLine($"🤖 Assistant Response:");
    if (assistantMessage.Content is TextContentPart content)
    {
        Console.WriteLine($"   💬 Message: {content.Text}");
    }

    // Step 2: Check if assistant wants to call tools
    if (assistantMessage.ToolCalls?.Count > 0)
    {
        Console.WriteLine($"\n🔧 Step 2: Assistant requested {assistantMessage.ToolCalls.Count} tool call(s):");

        var conversationMessages = new List<OpenAIChatMessage>(request.Messages)
        {
            assistantMessage
        };

        foreach (var toolCall in assistantMessage.ToolCalls)
        {
            var functionName = toolCall.Function?.Name;
            var arguments = toolCall.Function?.Arguments;

            Console.WriteLine($"   🛠️  Calling {functionName} with arguments: {arguments}");

            // Simulate tool execution
            string toolResult = functionName switch
            {
                "get_weather" => "{\"location\": \"Seoul\", \"temperature\": \"15°C\", \"condition\": \"Partly cloudy\", \"humidity\": \"65%\"}",
                "get_time" => $"{{\"current_time\": \"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\", \"timezone\": \"Local\"}}",
                _ => "{\"error\": \"Tool not found\"}"
            };

            Console.WriteLine($"   📋 Tool result: {toolResult}");

            // Add tool response to conversation
            conversationMessages.Add(new OpenAIChatMessage
            {
                Role = "tool",
                Content = new TextContentPart { Text = toolResult },
                ToolCallId = toolCall.Id
            });
        }

        // Step 3: Ask assistant to provide final response
        Console.WriteLine("\n🤖 Step 3: Getting final response from assistant...");

        var finalRequest = new OpenAIChatCompletionRequest
        {
            Model = modelKey,
            Messages = conversationMessages,
            MaxCompletionTokens = 200,
            Temperature = 0.1f
        };

        var finalResponse = await depot.CreateChatCompletionAsync(finalRequest);
        var finalMessage = finalResponse.Choices[0].Message;

        if (finalMessage.Content is TextContentPart finalContent)
        {
            Console.WriteLine($"🎯 Final Answer: {finalContent.Text}");
        }
    }
    else
    {
        Console.WriteLine("ℹ️  No tool calls requested by assistant.");
    }

    Console.WriteLine("\n✅ Tool workflow completed successfully!");
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Error: {ex.Message}");
    if (ex.InnerException != null)
    {
        Console.WriteLine($"   Inner: {ex.InnerException.Message}");
    }
}

Console.WriteLine("\n🎯 Demo completed. Press any key to exit...");
Console.ReadKey();
