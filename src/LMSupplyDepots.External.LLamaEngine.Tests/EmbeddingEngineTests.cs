using LLama;
using LLama.Common;
using LMSupplyDepots.External.LLamaEngine.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace LMSupplyDepots.External.LLamaEngine.Tests;

/// <summary>
/// Low-level tests for embedding functionality using LLama.cpp
/// </summary>
public class EmbeddingEngineTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ILogger<EmbeddingEngineTests> _logger;
    private readonly ServiceProvider _serviceProvider;
    private readonly string _testModelPath;

    public EmbeddingEngineTests(ITestOutputHelper output)
    {
        _output = output;

        // Test model path - using nomic embedding model
        _testModelPath = @"D:\filer-data\models\nomic-ai_nomic-embed-text-v2-moe-GGUF\nomic-embed-text-v2-moe.Q2_K.gguf";

        // Set up DI container with minimal dependencies
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));

        _serviceProvider = services.BuildServiceProvider();
        _logger = _serviceProvider.GetRequiredService<ILogger<EmbeddingEngineTests>>();
    }

    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task DirectLLamaEmbedder_ShouldCreateEmbeddings()
    {
        // Skip if model file doesn't exist
        if (!File.Exists(_testModelPath))
        {
            _output.WriteLine($"Skipping test: Model file not found at {_testModelPath}");
            return;
        }

        _output.WriteLine($"Testing direct LLama embedder with model: {_testModelPath}");

        try
        {
            // Create model parameters for embedding
            var modelParams = new ModelParams(_testModelPath)
            {
                Embeddings = true,
                ContextSize = 2048,
                BatchSize = 512,
                UBatchSize = 512 // Must match BatchSize for embeddings
            };

            _output.WriteLine("Loading LLama weights...");
            using var weights = LLamaWeights.LoadFromFile(modelParams);

            _output.WriteLine("Creating LLama embedder...");
            using var embedder = new LLamaEmbedder(weights, modelParams);

            var testText = "Hello, world! This is a test sentence for embedding generation.";
            _output.WriteLine($"Generating embedding for text: '{testText}'");

            var embeddings = await embedder.GetEmbeddings(testText);

            // Assertions
            Assert.NotEmpty(embeddings);
            Assert.Single(embeddings);

            var embedding = embeddings[0];
            Assert.NotEmpty(embedding);
            Assert.True(embedding.Length > 0);

            _output.WriteLine($"Successfully generated embedding with dimension: {embedding.Length}");
            _output.WriteLine($"First 10 values: [{string.Join(", ", embedding.Take(10).Select(x => x.ToString("F6")))}]");

            // Verify embedding values are reasonable
            Assert.True(embedding.Any(x => x != 0), "Embedding should not be all zeros");
            Assert.True(embedding.All(x => !float.IsNaN(x) && !float.IsInfinity(x)), "Embedding should not contain NaN or Infinity");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Test failed with exception: {ex.Message}");
            _output.WriteLine($"Stack trace: {ex.StackTrace}");
            throw;
        }
    }

    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task DirectLLamaEmbedder_ShouldCreateMultipleEmbeddings()
    {
        // Skip if model file doesn't exist
        if (!File.Exists(_testModelPath))
        {
            _output.WriteLine($"Skipping test: Model file not found at {_testModelPath}");
            return;
        }

        _output.WriteLine($"Testing multiple embeddings with model: {_testModelPath}");

        try
        {
            var modelParams = new ModelParams(_testModelPath)
            {
                Embeddings = true,
                ContextSize = 2048,
                BatchSize = 512,
                UBatchSize = 512
            };

            using var weights = LLamaWeights.LoadFromFile(modelParams);
            using var embedder = new LLamaEmbedder(weights, modelParams);

            var testTexts = new[]
            {
                "Hello, world!",
                "This is a test sentence.",
                "Another example text for embedding."
            };

            var allEmbeddings = new List<float[]>();

            foreach (var text in testTexts)
            {
                _output.WriteLine($"Generating embedding for: '{text}'");
                var embeddings = await embedder.GetEmbeddings(text);

                Assert.NotEmpty(embeddings);
                Assert.Single(embeddings);

                allEmbeddings.Add(embeddings[0]);
            }

            // Verify all embeddings have the same dimension
            var firstDimension = allEmbeddings[0].Length;
            Assert.True(allEmbeddings.All(e => e.Length == firstDimension),
                "All embeddings should have the same dimension");

            // Verify embeddings are different (not identical)
            for (int i = 0; i < allEmbeddings.Count - 1; i++)
            {
                for (int j = i + 1; j < allEmbeddings.Count; j++)
                {
                    var similarity = CosineSimilarity(allEmbeddings[i], allEmbeddings[j]);
                    _output.WriteLine($"Similarity between embedding {i} and {j}: {similarity:F6}");

                    // Embeddings should be similar but not identical
                    Assert.True(similarity < 0.99, "Embeddings should not be identical");
                }
            }

            _output.WriteLine($"Successfully generated {allEmbeddings.Count} embeddings with dimension {firstDimension}");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Test failed with exception: {ex.Message}");
            throw;
        }
    }

    [Fact]
    [Trait("Category", "LocalIntegration")]
    public void ModelParams_ShouldValidate_EmbeddingConfiguration()
    {
        // Skip if model file doesn't exist
        if (!File.Exists(_testModelPath))
        {
            _output.WriteLine($"Skipping test: Model file not found at {_testModelPath}");
            return;
        }

        // Test various parameter configurations
        var validParams = new ModelParams(_testModelPath)
        {
            Embeddings = true,
            ContextSize = 2048,
            BatchSize = 512,
            UBatchSize = 512
        };

        // Should not throw
        Assert.NotNull(validParams);
        Assert.True(validParams.Embeddings);
        Assert.Equal(512u, validParams.BatchSize);
        Assert.Equal(512u, validParams.UBatchSize);

        _output.WriteLine("Model parameters validation passed");
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Vectors must have the same length");

        float dotProduct = 0;
        float magnitudeA = 0;
        float magnitudeB = 0;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            magnitudeA += a[i] * a[i];
            magnitudeB += b[i] * b[i];
        }

        magnitudeA = (float)Math.Sqrt(magnitudeA);
        magnitudeB = (float)Math.Sqrt(magnitudeB);

        if (magnitudeA == 0 || magnitudeB == 0)
            return 0;

        return dotProduct / (magnitudeA * magnitudeB);
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }
}
