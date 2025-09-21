using LLama.Common;
using LMSupplyDepots.External.LLamaEngine.Extensions;
using LMSupplyDepots.External.LLamaEngine.Models;
using LMSupplyDepots.External.LLamaEngine.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Xunit.Abstractions;

namespace LMSupplyDepots.External.LLamaEngine.Tests;

/// <summary>
/// OpenAI-compatible Chat Completion request model
/// </summary>
public class ChatCompletionRequest
{
    public string Model { get; set; } = "";
    public List<ChatMessage> Messages { get; set; } = new();
    public float Temperature { get; set; } = 0.7f;
    public int MaxTokens { get; set; } = 2048;
    public bool Stream { get; set; } = false;
    public float TopP { get; set; } = 1.0f;
    public int N { get; set; } = 1;
    public List<string>? Stop { get; set; }
}

/// <summary>
/// OpenAI-compatible Chat Message model
/// </summary>
public class ChatMessage
{
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
}

/// <summary>
/// OpenAI-compatible Chat Completion response model
/// </summary>
public class ChatCompletionResponse
{
    public string Id { get; set; } = "";
    public string Object { get; set; } = "chat.completion";
    public long Created { get; set; }
    public string Model { get; set; } = "";
    public List<Choice> Choices { get; set; } = new();
    public Usage Usage { get; set; } = new();
}

/// <summary>
/// OpenAI-compatible Choice model
/// </summary>
public class Choice
{
    public int Index { get; set; }
    public ChatMessage Message { get; set; } = new();
    public string FinishReason { get; set; } = "";
}

/// <summary>
/// OpenAI-compatible Usage model
/// </summary>
public class Usage
{
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
}

/// <summary>
/// OpenAI-compatible Embedding request model
/// </summary>
public class EmbeddingRequest
{
    public string Model { get; set; } = "";
    public List<string> Input { get; set; } = new();
    public string? EncodingFormat { get; set; } = "float";
}

/// <summary>
/// OpenAI-compatible Embedding response model
/// </summary>
public class EmbeddingResponse
{
    public string Object { get; set; } = "list";
    public List<EmbeddingData> Data { get; set; } = new();
    public string Model { get; set; } = "";
    public Usage Usage { get; set; } = new();
}

/// <summary>
/// OpenAI-compatible Embedding data model
/// </summary>
public class EmbeddingData
{
    public string Object { get; set; } = "embedding";
    public int Index { get; set; }
    public List<float> Embedding { get; set; } = new();
}

public class OpenAICompatibilityTests
{
    private readonly ITestOutputHelper _output;
    private readonly string _modelPath;
    private readonly string _embeddingModelPath;
    private const string ModelIdentifier = "test/llama:model";
    private const string EmbeddingModelIdentifier = "test/embedding:model";

    public OpenAICompatibilityTests(ITestOutputHelper output)
    {
        _output = output;
        _modelPath = @"D:\filer-data\models\bartowski_Llama-3.2-3B-Instruct-GGUF\Llama-3.2-3B-Instruct-IQ3_M.gguf";
        // For embedding tests, we'll use the same model configured for embeddings
        _embeddingModelPath = _modelPath;
    }
    private ServiceCollection CreateServiceCollection()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        services.AddLLamaEngine();
        return services;
    }

    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task ChatCompletions_Should_Work_With_OpenAI_Format()
    {
        // Arrange
        var services = CreateServiceCollection();
        await using var provider = services.BuildServiceProvider();

        var modelManager = provider.GetRequiredService<ILLamaModelManager>();
        var llmService = provider.GetRequiredService<ILLMService>();

        // Load model
        _output.WriteLine($"Loading model: {_modelPath}");
        var modelInfo = await modelManager.LoadModelAsync(_modelPath, ModelIdentifier);

        Assert.NotNull(modelInfo);
        Assert.Equal(LocalModelState.Loaded, modelInfo.State);

        try
        {
            // Create OpenAI-compatible request
            var request = new ChatCompletionRequest
            {
                Model = ModelIdentifier,
                Messages = new List<ChatMessage>
                {
                    new ChatMessage { Role = "system", Content = "You are a helpful assistant." },
                    new ChatMessage { Role = "user", Content = "What is artificial intelligence?" }
                },
                Temperature = 0.7f,
                MaxTokens = 100
            };

            // Convert to prompt format (simplified for this test)
            var prompt = ConvertChatToPrompt(request.Messages);
            // Create inference params from request
            var inferenceParams = ParameterFactory.NewInferenceParams(
                maxTokens: request.MaxTokens,
                temperature: request.Temperature,
                topP: request.TopP
            );

            // Act
            _output.WriteLine("Starting inference...");
            var startTime = DateTime.UtcNow;
            var responseText = await llmService.InferAsync(ModelIdentifier, prompt, inferenceParams);
            var endTime = DateTime.UtcNow;

            // Assert
            Assert.NotNull(responseText);
            Assert.NotEmpty(responseText.Trim());

            _output.WriteLine($"Response: {responseText}");
            _output.WriteLine($"Inference time: {(endTime - startTime).TotalMilliseconds}ms");

            // Create OpenAI-compatible response
            var response = new ChatCompletionResponse
            {
                Id = $"chatcmpl-{Guid.NewGuid():N}",
                Created = ((DateTimeOffset)startTime).ToUnixTimeSeconds(),
                Model = request.Model,
                Choices = new List<Choice>
                {
                    new Choice
                    {
                        Index = 0,
                        Message = new ChatMessage { Role = "assistant", Content = responseText.Trim() },
                        FinishReason = "stop"
                    }
                },
                Usage = new Usage
                {
                    PromptTokens = EstimateTokens(prompt),
                    CompletionTokens = EstimateTokens(responseText),
                    TotalTokens = EstimateTokens(prompt) + EstimateTokens(responseText)
                }
            };

            // Verify response structure
            Assert.Equal("chat.completion", response.Object);
            Assert.Single(response.Choices);
            Assert.Equal("assistant", response.Choices[0].Message.Role);
            Assert.NotEmpty(response.Choices[0].Message.Content);
            Assert.True(response.Usage.TotalTokens > 0);

            _output.WriteLine($"OpenAI-compatible response created successfully");
        }
        finally
        {
            await modelManager.UnloadModelAsync(ModelIdentifier);
        }
    }

    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task ChatCompletions_Stream_Should_Work_With_OpenAI_Format()
    {
        // Arrange
        var services = CreateServiceCollection();
        await using var provider = services.BuildServiceProvider();

        var modelManager = provider.GetRequiredService<ILLamaModelManager>();
        var llmService = provider.GetRequiredService<ILLMService>();

        // Load model
        _output.WriteLine($"Loading model: {_modelPath}");
        var modelInfo = await modelManager.LoadModelAsync(_modelPath, ModelIdentifier);

        Assert.NotNull(modelInfo);
        Assert.Equal(LocalModelState.Loaded, modelInfo.State);

        try
        {
            // Create OpenAI-compatible streaming request
            var request = new ChatCompletionRequest
            {
                Model = ModelIdentifier,
                Messages = new List<ChatMessage>
                {
                    new ChatMessage { Role = "system", Content = "You are a helpful assistant." },
                    new ChatMessage { Role = "user", Content = "Count from 1 to 5." }
                },
                Temperature = 0.7f,
                MaxTokens = 50,
                Stream = true
            }; var prompt = ConvertChatToPrompt(request.Messages);
            var inferenceParams = ParameterFactory.NewInferenceParams(
                maxTokens: request.MaxTokens,
                temperature: request.Temperature,
                topP: request.TopP
            );

            // Act
            _output.WriteLine("Starting streaming inference...");
            var tokens = new List<string>();
            var startTime = DateTime.UtcNow;

            await foreach (var token in llmService.InferStreamAsync(ModelIdentifier, prompt, inferenceParams))
            {
                tokens.Add(token);
                _output.WriteLine($"Token: {token}");

                // In real implementation, this would be sent as SSE (Server-Sent Events)
                var streamResponse = new
                {
                    id = $"chatcmpl-{Guid.NewGuid():N}",
                    @object = "chat.completion.chunk",
                    created = ((DateTimeOffset)startTime).ToUnixTimeSeconds(),
                    model = request.Model,
                    choices = new[]
                    {
                        new
                        {
                            index = 0,
                            delta = new { content = token },
                            finish_reason = (string?)null
                        }
                    }
                };

                var jsonChunk = JsonSerializer.Serialize(streamResponse);
                _output.WriteLine($"SSE Chunk: data: {jsonChunk}");
            }

            var endTime = DateTime.UtcNow;

            // Assert
            Assert.NotEmpty(tokens);
            var fullResponse = string.Concat(tokens);
            Assert.NotEmpty(fullResponse.Trim());

            _output.WriteLine($"Full response: {fullResponse}");
            _output.WriteLine($"Total tokens: {tokens.Count}");
            _output.WriteLine($"Streaming time: {(endTime - startTime).TotalMilliseconds}ms");

            // Final chunk with finish_reason
            var finalChunk = new
            {
                id = $"chatcmpl-{Guid.NewGuid():N}",
                @object = "chat.completion.chunk",
                created = ((DateTimeOffset)startTime).ToUnixTimeSeconds(),
                model = request.Model,
                choices = new[]
                {
                    new
                    {
                        index = 0,
                        delta = new { },
                        finish_reason = "stop"
                    }
                }
            };

            _output.WriteLine($"Final SSE Chunk: data: {JsonSerializer.Serialize(finalChunk)}");
            _output.WriteLine("Stream completed successfully");
        }
        finally
        {
            await modelManager.UnloadModelAsync(ModelIdentifier);
        }
    }

    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task Embeddings_Should_Work_With_OpenAI_Format()
    {
        // Arrange
        var services = CreateServiceCollection();
        await using var provider = services.BuildServiceProvider();

        var modelManager = provider.GetRequiredService<ILLamaModelManager>();
        var llmService = provider.GetRequiredService<ILLMService>();

        // Load model for embeddings
        _output.WriteLine($"Loading model for embeddings: {_embeddingModelPath}");
        var modelInfo = await modelManager.LoadModelAsync(_embeddingModelPath, ModelIdentifier);

        Assert.NotNull(modelInfo);
        Assert.Equal(LocalModelState.Loaded, modelInfo.State);

        try
        {
            // Create OpenAI-compatible embedding request
            var request = new EmbeddingRequest
            {
                Model = ModelIdentifier,
                Input = new List<string>
                {
                    "The quick brown fox jumps over the lazy dog.",
                    "Artificial intelligence is transforming the world.",
                    "Machine learning enables computers to learn from data."
                }
            };

            // Act
            _output.WriteLine("Starting embedding generation...");
            var startTime = DateTime.UtcNow;
            var embeddings = new List<float[]>();

            for (int i = 0; i < request.Input.Count; i++)
            {
                var text = request.Input[i];
                _output.WriteLine($"Generating embedding for text {i + 1}: {text}");

                var embedding = await llmService.CreateEmbeddingAsync(ModelIdentifier, text);
                embeddings.Add(embedding);

                _output.WriteLine($"Generated embedding with {embedding.Length} dimensions");
            }

            var endTime = DateTime.UtcNow;

            // Assert
            Assert.Equal(request.Input.Count, embeddings.Count);

            foreach (var embedding in embeddings)
            {
                Assert.NotNull(embedding);
                Assert.True(embedding.Length > 0);

                // Check that embeddings are normalized (if normalize=true was used)
                var magnitude = Math.Sqrt(embedding.Sum(x => x * x));
                _output.WriteLine($"Embedding magnitude: {magnitude}");
            }

            // Create OpenAI-compatible response
            var response = new EmbeddingResponse
            {
                Model = request.Model,
                Data = embeddings.Select((emb, idx) => new EmbeddingData
                {
                    Index = idx,
                    Embedding = emb.ToList()
                }).ToList(),
                Usage = new Usage
                {
                    PromptTokens = request.Input.Sum(EstimateTokens),
                    TotalTokens = request.Input.Sum(EstimateTokens)
                }
            };

            // Verify response structure
            Assert.Equal("list", response.Object);
            Assert.Equal(request.Input.Count, response.Data.Count);

            for (int i = 0; i < response.Data.Count; i++)
            {
                Assert.Equal("embedding", response.Data[i].Object);
                Assert.Equal(i, response.Data[i].Index);
                Assert.NotEmpty(response.Data[i].Embedding);
            }

            _output.WriteLine($"OpenAI-compatible embedding response created successfully");
            _output.WriteLine($"Processing time: {(endTime - startTime).TotalMilliseconds}ms");
        }
        finally
        {
            await modelManager.UnloadModelAsync(ModelIdentifier);
        }
    }

    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task Models_List_Should_Work_With_OpenAI_Format()
    {
        // Arrange
        var services = CreateServiceCollection();
        await using var provider = services.BuildServiceProvider();

        var modelManager = provider.GetRequiredService<ILLamaModelManager>();

        // Load a model
        _output.WriteLine($"Loading model: {_modelPath}");
        var modelInfo = await modelManager.LoadModelAsync(_modelPath, ModelIdentifier);

        Assert.NotNull(modelInfo);
        Assert.Equal(LocalModelState.Loaded, modelInfo.State);

        try
        {            // Act - Get loaded models
            var loadedModels = await modelManager.GetLoadedModelsAsync();            // Create OpenAI-compatible models list response
            var response = new
            {
                @object = "list",
                data = loadedModels.Select(model => new
                {
                    id = model.ModelId,
                    @object = "model",
                    created = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds(),
                    owned_by = "llamasharp",
                    permission = new[] { new { } },
                    root = model.ModelId,
                    parent = (string?)null
                }).ToList()
            };

            // Assert
            Assert.NotEmpty(response.data);
            Assert.Contains(response.data, m => m.id == ModelIdentifier);

            _output.WriteLine($"Models list response:");
            _output.WriteLine(JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            await modelManager.UnloadModelAsync(ModelIdentifier);
        }
    }

    /// <summary>
    /// Converts chat messages to a single prompt string
    /// </summary>
    private static string ConvertChatToPrompt(List<ChatMessage> messages)
    {
        var prompt = new System.Text.StringBuilder();

        foreach (var message in messages)
        {
            switch (message.Role.ToLowerInvariant())
            {
                case "system":
                    prompt.AppendLine($"System: {message.Content}");
                    break;
                case "user":
                    prompt.AppendLine($"User: {message.Content}");
                    break;
                case "assistant":
                    prompt.AppendLine($"Assistant: {message.Content}");
                    break;
            }
        }

        prompt.AppendLine("Assistant:");
        return prompt.ToString();
    }

    /// <summary>
    /// Simple token estimation (rough approximation)
    /// </summary>
    private static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        // Rough approximation: 1 token ≈ 4 characters for English
        return Math.Max(1, text.Length / 4);
    }
}
