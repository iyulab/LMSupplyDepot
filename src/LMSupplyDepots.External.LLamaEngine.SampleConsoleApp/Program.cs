using LMSupplyDepots.External.LLamaEngine;
using LMSupplyDepots.External.LLamaEngine.Services;
using LMSupplyDepots.External.LLamaEngine.Chat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using LMSupplyDepots.External.LLamaEngine.Extensions;

var modelPath = @"D:\filer-data\models\text-generation\MaziyarPanahi\Llama-3.2-1B-Instruct-GGUF\Llama-3.2-1B-Instruct.fp16.gguf";
var modelIdentifier = "MaziyarPanahi/Llama-3.2-1B-Instruct:Llama-3.2-1B-Instruct.fp16.gguf";

// DI 설정
var services = new ServiceCollection();
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});
services.AddLLamaEngine();

using var serviceProvider = services.BuildServiceProvider();
var modelManager = serviceProvider.GetRequiredService<ILLamaModelManager>();
var llmService = serviceProvider.GetRequiredService<ILLMService>();

// 모델 로드
Console.WriteLine("Loading LLM model...");
var modelInfo = await modelManager.LoadModelAsync(modelPath, modelIdentifier);
if (modelInfo?.State != LMSupplyDepots.External.LLamaEngine.Models.LocalModelState.Loaded)
{
    Console.WriteLine($"Failed to load model: {modelInfo?.LastError ?? "Unknown error"}");
    return;
}

var inferenceParams = ParameterFactory.NewInferenceParams();

// Initialize chat history
string systemPrompt = @"You are a helpful AI assistant that speaks Korean.
Provide clear and helpful answers, but be honest about what you don't know.
Your answers are always in Korean.</s>";

var chatHistory = new ChatHistory(systemPrompt);

Console.WriteLine("\nChat started. Type 'exit' to quit.");
Console.WriteLine("Type 'clear' to clear chat history.");
Console.WriteLine("Type 'regenerate' to regenerate the last response.");
Console.WriteLine("-------------------");

try
{
    while (true)
    {
        Console.Write("\nUser: ");
        var input = Console.ReadLine();

        if (string.IsNullOrEmpty(input))
            continue;

        switch (input.ToLower())
        {
            case "exit":
                return;

            case "clear":
                chatHistory.Clear();
                Console.WriteLine("Chat history cleared.");
                continue;

            case "regenerate":
                // Remove last assistant message if exists
                var messages = chatHistory.Messages;
                if (messages.Count > 1 && messages[^1].Role == "assistant")
                {
                    // Skip regeneration and continue with next input
                    continue;
                }
                break;
        }

        // Add user message to history
        chatHistory.AddMessage("user", input);

        // Get complete prompt with history
        var fullPrompt = chatHistory.GetFormattedPrompt();

        Console.Write("Assistant: ");
        try
        {
            var response = new System.Text.StringBuilder();

            await foreach (var text in llmService.InferStreamAsync(
                modelIdentifier,
                fullPrompt,
                inferenceParams))
            {
                Console.Write(text);
                response.Append(text);
            }

            // Add assistant's response to history
            chatHistory.AddMessage("assistant", response.ToString());
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nError occurred during inference: {ex.Message}");
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"\nError occurred: {ex.Message}");
}
finally
{
    // 모델 언로드
    await modelManager.UnloadModelAsync(modelIdentifier);
}

Console.WriteLine("\nChat ended. Press any key to exit.");
Console.ReadKey();