// using LMSupplyDepots.SDK;
// using LMSupplyDepots.Contracts;
// using LMSupplyDepots.ModelHub.Models;

// namespace LMSupplyDepots.SampleApp;

// public class Program
// {
//     private static LMSupplyDepot? _depot;

//     public static async Task Main(string[] args)
//     {
//         Console.WriteLine("LMSupplyDepot Embedding Sample");
//         Console.WriteLine("================================");

//         try
//         {
//             // Set up the depot with specified DataPath for model download
//             var downloadOptions = new LMSupplyDepotOptions
//             {
//                 DataPath = @"D:\filer-data"
//             };

//             Console.WriteLine("Attempting to download nomic embedding model...");
//             Console.WriteLine("===============================================");

//             using var downloadDepot = new LMSupplyDepot(downloadOptions);

//             // Define the model to download
//             var repoId = "nomic-ai/nomic-embed-text-v2-moe-GGUF";
//             var artifactName = "nomic-embed-text-v2-moe.Q2_K";
//             var modelKey = $"hf:{repoId}/{artifactName}";

//             Console.WriteLine($"Model to download: {repoId}");
//             Console.WriteLine($"Artifact: {artifactName}");
//             Console.WriteLine($"Download path: {downloadOptions.DataPath}");
//             Console.WriteLine();

//             // Check if model already exists
//             var existingModels = await downloadDepot.ListModelsAsync();
//             var existingModel = existingModels.FirstOrDefault(m => m.Id.Contains(repoId));

//             if (existingModel != null)
//             {
//                 Console.WriteLine("Model already exists! Using existing model...");
//                 _depot = downloadDepot;
//             }
//             else
//             {
//                 Console.WriteLine("Starting model download...");

//                 // Create progress reporter
//                 var progress = new Progress<ModelDownloadProgress>(p =>
//                 {
//                     if (p.TotalBytes.HasValue && p.TotalBytes > 0)
//                     {
//                         var percentage = (double)p.BytesDownloaded / p.TotalBytes.Value * 100;
//                         Console.WriteLine($"Download progress: {percentage:F1}% ({FormatBytes(p.BytesDownloaded)}/{FormatBytes(p.TotalBytes.Value)}) - {p.FileName}");
//                     }
//                     else
//                     {
//                         Console.WriteLine($"Downloaded: {FormatBytes(p.BytesDownloaded)} - {p.FileName}");
//                     }
//                 });

//                 // Download the model
//                 var downloadedModel = await downloadDepot.DownloadModelAsync(modelKey, progress);

//                 Console.WriteLine($"\nModel downloaded successfully!");
//                 Console.WriteLine($"Model ID: {downloadedModel.Id}");
//                 Console.WriteLine($"Size: {FormatBytes(downloadedModel.SizeInBytes)}");
//                 Console.WriteLine($"Local path: {downloadedModel.LocalPath}");

//                 _depot = downloadDepot;
//             }

//             // List available models
//             Console.WriteLine("\nListing available models...");
//             var models = await _depot.ListModelsAsync();

//             Console.WriteLine($"Found {models.Count} models:");
//             foreach (var model in models)
//             {
//                 Console.WriteLine($"- {model.Id} ({model.Type}) - {(model.Capabilities.SupportsEmbeddings ? "Supports Embeddings" : "No Embeddings")}");
//             }

//             // Find an embedding model
//             var embeddingModel = models.FirstOrDefault(m => m.Capabilities.SupportsEmbeddings);
//             if (embeddingModel == null)
//             {
//                 Console.WriteLine("No embedding models found. Looking for any model to demonstrate loading...");
//                 embeddingModel = models.FirstOrDefault();
//                 if (embeddingModel == null)
//                 {
//                     Console.WriteLine("No models found at all!");
//                     return;
//                 }
//             }

//             var embeddingModelId = embeddingModel.Id;

//             Console.WriteLine($"Loading model: {embeddingModelId}");
//             await _depot.LoadModelAsync(embeddingModelId);
//             Console.WriteLine("Model loaded successfully!");

//             // If this model doesn't support embeddings, just show that it was loaded
//             if (!embeddingModel.Capabilities.SupportsEmbeddings)
//             {
//                 Console.WriteLine($"Model {embeddingModelId} doesn't support embeddings, but was loaded successfully.");
//                 Console.WriteLine("This demonstrates the model loading functionality.");

//                 // Try to generate some text instead
//                 try
//                 {
//                     Console.WriteLine("\nTrying text generation instead...");
//                     var textResponse = await _depot.GenerateTextAsync(embeddingModelId, "Hello, world!");
//                     Console.WriteLine($"Generated text: {textResponse.Text}");
//                 }
//                 catch (Exception textEx)
//                 {
//                     Console.WriteLine($"Text generation failed: {textEx.Message}");
//                 }

//                 return;
//             }

//             // Test texts for embedding
//             var testTexts = new List<string>
//             {
//                 "The quick brown fox jumps over the lazy dog.",
//                 "Artificial intelligence is transforming the world.",
//                 "Machine learning models can process natural language.",
//                 "Vector embeddings capture semantic meaning of text."
//             };

//             Console.WriteLine("\nGenerating embeddings for test texts:");
//             Console.WriteLine("====================================");

//             // Create embedding request
//             var embeddingRequest = new EmbeddingRequest
//             {
//                 Texts = testTexts,
//                 Normalize = true
//             };

//             var response = await _depot.GenerateEmbeddingsAsync(embeddingModelId, embeddingRequest);

//             Console.WriteLine($"Generated {response.Embeddings.Count} embeddings");
//             Console.WriteLine($"Embedding dimension: {response.Dimension}");
//             Console.WriteLine($"Total tokens processed: {response.TotalTokens}");
//             Console.WriteLine($"Processing time: {response.ElapsedTime.TotalMilliseconds:F2}ms");

//             for (int i = 0; i < testTexts.Count; i++)
//             {
//                 var embedding = response.Embeddings[i];
//                 Console.WriteLine($"\nText {i + 1}: {testTexts[i]}");
//                 Console.WriteLine($"First 5 values: [{string.Join(", ", embedding.Take(5).Select(v => v.ToString("F4")))}]");
//             }

//             // Demonstrate similarity calculation
//             Console.WriteLine("\nCalculating similarities:");
//             Console.WriteLine("========================");

//             var embedding1 = response.Embeddings[0];
//             var embedding2 = response.Embeddings[1];
//             var embedding3 = response.Embeddings[2];

//             var similarity12 = CalculateCosineSimilarity(embedding1, embedding2);
//             var similarity23 = CalculateCosineSimilarity(embedding2, embedding3);

//             Console.WriteLine($"Similarity between text 1 and 2: {similarity12:F4}");
//             Console.WriteLine($"Similarity between text 2 and 3: {similarity23:F4}");

//             Console.WriteLine("\nEmbedding generation completed successfully!");
//         }
//         catch (Exception ex)
//         {
//             Console.WriteLine($"Error: {ex.Message}");
//             if (ex.InnerException != null)
//             {
//                 Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
//             }
//             Console.WriteLine($"Stack trace: {ex.StackTrace}");

//             // Fall back to mock demonstration
//             await ShowMockEmbeddingDemonstrationAsync();
//         }
//         finally
//         {
//             if (_depot != null)
//             {
//                 _depot.Dispose();
//                 Console.WriteLine("\nLMSupplyDepot disposed.");
//             }
//         }

//         Console.WriteLine("\nPress any key to exit...");
//         Console.ReadKey();
//     }

//     private static float CalculateCosineSimilarity(float[] vector1, float[] vector2)
//     {
//         if (vector1.Length != vector2.Length)
//             throw new ArgumentException("Vectors must have the same dimension");

//         float dotProduct = 0;
//         float magnitude1 = 0;
//         float magnitude2 = 0;

//         for (int i = 0; i < vector1.Length; i++)
//         {
//             dotProduct += vector1[i] * vector2[i];
//             magnitude1 += vector1[i] * vector1[i];
//             magnitude2 += vector2[i] * vector2[i];
//         }

//         magnitude1 = MathF.Sqrt(magnitude1);
//         magnitude2 = MathF.Sqrt(magnitude2);

//         if (magnitude1 == 0 || magnitude2 == 0)
//             return 0;

//         return dotProduct / (magnitude1 * magnitude2);
//     }

//     private static string FormatBytes(long bytes)
//     {
//         if (bytes < 1024) return $"{bytes} B";
//         if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
//         if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
//         return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
//     }

//     private static async Task ShowMockEmbeddingDemonstrationAsync()
//     {
//         Console.WriteLine("\nDemonstrating the embedding functionality structure:");
//         Console.WriteLine("====================================================");

//         // Create a mock demonstration of what the app would do
//         var mockTexts = new List<string>
//         {
//             "The quick brown fox jumps over the lazy dog.",
//             "Artificial intelligence is transforming the world.",
//             "Machine learning models can process natural language.",
//             "Vector embeddings capture semantic meaning of text."
//         };

//         Console.WriteLine("\nSample texts for embedding:");
//         for (int i = 0; i < mockTexts.Count; i++)
//         {
//             Console.WriteLine($"{i + 1}. {mockTexts[i]}");
//         }

//         // Mock embedding vectors (in reality, these would come from the model)
//         var mockEmbeddings = new List<float[]>
//         {
//             new float[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f },
//             new float[] { 0.15f, 0.25f, 0.35f, 0.45f, 0.55f, 0.65f, 0.75f, 0.85f },
//             new float[] { 0.12f, 0.22f, 0.32f, 0.42f, 0.52f, 0.62f, 0.72f, 0.82f },
//             new float[] { 0.18f, 0.28f, 0.38f, 0.48f, 0.58f, 0.68f, 0.78f, 0.88f }
//         };

//         Console.WriteLine("\nMock embedding generation (what would happen with a real model):");
//         Console.WriteLine($"Generated {mockEmbeddings.Count} embeddings");
//         Console.WriteLine($"Embedding dimension: {mockEmbeddings[0].Length}");

//         for (int i = 0; i < mockTexts.Count; i++)
//         {
//             var embedding = mockEmbeddings[i];
//             Console.WriteLine($"\nText {i + 1}: {mockTexts[i]}");
//             Console.WriteLine($"Embedding: [{string.Join(", ", embedding.Select(v => v.ToString("F3")))}]");
//         }

//         // Demonstrate similarity calculation with mock data
//         Console.WriteLine("\nCalculating similarities with mock embeddings:");
//         Console.WriteLine("==============================================");

//         var mockSimilarity12 = CalculateCosineSimilarity(mockEmbeddings[0], mockEmbeddings[1]);
//         var mockSimilarity23 = CalculateCosineSimilarity(mockEmbeddings[1], mockEmbeddings[2]);
//         var mockSimilarity13 = CalculateCosineSimilarity(mockEmbeddings[0], mockEmbeddings[2]);

//         Console.WriteLine($"Similarity between text 1 and 2: {mockSimilarity12:F4}");
//         Console.WriteLine($"Similarity between text 2 and 3: {mockSimilarity23:F4}");
//         Console.WriteLine($"Similarity between text 1 and 3: {mockSimilarity13:F4}");

//         Console.WriteLine("\nThis demonstrates the complete embedding workflow:");
//         Console.WriteLine("1. Text preprocessing");
//         Console.WriteLine("2. Model inference for embedding generation");
//         Console.WriteLine("3. Vector similarity calculations");
//         Console.WriteLine("4. Results analysis and display");

//         Console.WriteLine("\nTo run with real models:");
//         Console.WriteLine("- Place model files in a supported directory");
//         Console.WriteLine("- Ensure the model supports embedding generation");
//         Console.WriteLine("- Configure the DataPath in LMSupplyDepotOptions");

//         await Task.CompletedTask; // Make it async
//     }
// }
