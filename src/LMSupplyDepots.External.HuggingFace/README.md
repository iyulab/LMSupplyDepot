# LMSupplyDepots.External.HuggingFace

A .NET library for interacting with the Hugging Face Hub API. It focuses on downloading and managing machine learning models in GGUF format, providing features such as concurrent downloads, progress tracking, and download resumption.

## Key Features

- **Model Search**: 
  - Search for text generation models and embedding models on Hugging Face Hub
  - Support for various filtering and sorting options
  - Automatic filtering for GGUF format models
  
- **Download Management**: 
  - Configurable concurrent download limits
  - Detailed progress tracking and reporting
  - Resume interrupted downloads
  - Automatic retry on failure
  - Efficient buffer management for large files

- **File Management**:
  - Specialized support for GGUF model files
  - File size information retrieval
  - Repository structure exploration
  - Support for both single-file and multi-file models
  - Handling of split files and subdirectories

- **Error Handling**:
  - Automatic retry with exponential backoff
  - Detailed error reporting
  - Authentication error detection

## Usage

### Basic Setup

```csharp
// Create client options
var options = new HuggingFaceClientOptions
{
    Token = "your-token", // Optional: Required for private models
    MaxConcurrentDownloads = 3,
    ProgressUpdateInterval = 100,
    MaxRetries = 3
};

// Create client
using var client = new HuggingFaceClient(options);
```

### Search for Text Generation Models

```csharp
// Search for text generation models
var models = await client.SearchTextGenerationModelsAsync(
    search: "llama",
    filters: null,
    limit: 5,
    sortField: ModelSortField.Downloads,
    descending: true);

foreach (var model in models)
{
    Console.WriteLine($"Model: {model.ModelId}");
    Console.WriteLine($"Downloads: {model.Downloads}");
}
```

### Search for Embedding Models

```csharp
// Search for embedding models
var embeddingModels = await client.SearchEmbeddingModelsAsync(
    search: "sentence-transformers",
    limit: 5);

foreach (var model in embeddingModels)
{
    Console.WriteLine($"Model: {model.ModelId}");
    Console.WriteLine($"Author: {model.Author}");
}
```

### Find Model by Repository ID

```csharp
// Find a specific model by repository ID
string repoId = "thebloke/Llama-2-7B-GGUF";
var model = await client.FindModelByRepoIdAsync(repoId);

Console.WriteLine($"Model: {model.ModelId}");
Console.WriteLine($"Last Modified: {model.LastModified}");
Console.WriteLine($"Number of files: {model.Siblings?.Count ?? 0}");
```

### Download a Single File

```csharp
// Download a specific file
string repoId = "thebloke/Llama-2-7B-GGUF";
string filePath = "llama-2-7b.Q4_K_M.gguf";
string outputPath = "path/to/output/file.gguf";

var result = await client.DownloadFileWithResultAsync(
    repoId, 
    filePath, 
    outputPath, 
    progress: new Progress<FileDownloadProgress>(p => 
    {
        Console.WriteLine($"Progress: {p.FormattedProgress}");
        Console.WriteLine($"Download Speed: {p.FormattedDownloadSpeed}");
    }));

Console.WriteLine($"Download completed: {result.IsCompleted}");
Console.WriteLine($"Downloaded size: {result.BytesDownloaded} bytes");
```

### Download a Repository

```csharp
// Download all files from a repository
string repoId = "thebloke/Llama-2-7B-GGUF";
string outputDir = "path/to/output/directory";

await foreach (var progress in client.DownloadRepositoryAsync(repoId, outputDir))
{
    Console.WriteLine($"Total Progress: {progress.TotalProgress:P0}");
    Console.WriteLine($"Completed Files: {progress.CompletedFiles.Count}/{progress.TotalFiles.Count}");
    
    foreach (var fileProgress in progress.CurrentProgresses)
    {
        Console.WriteLine($"  File: {Path.GetFileName(fileProgress.UploadPath)} - {fileProgress.FormattedProgress}");
    }
}
```

### Download Specific Files

```csharp
// Download specific files from a repository
string repoId = "thebloke/Llama-2-7B-GGUF";
string[] filesToDownload = ["llama-2-7b.Q4_K_M.gguf", "config.json"];
string outputDir = "path/to/output/directory";

await foreach (var progress in client.DownloadRepositoryFilesAsync(repoId, filesToDownload, outputDir))
{
    Console.WriteLine($"Total Progress: {progress.TotalProgress:P0}");
    Console.WriteLine($"Completed Files: {progress.CompletedFiles.Count}/{progress.TotalFiles.Count}");
}
```

### Get Repository File Sizes

```csharp
// Get the size of files in a repository
string repoId = "thebloke/Llama-2-7B-GGUF";
var fileSizes = await client.GetRepositoryFileSizesAsync(repoId);

foreach (var file in fileSizes)
{
    Console.WriteLine($"File: {file.Key}, Size: {Common.StringFormatter.FormatSize(file.Value)}");
}
```

### Get Model Artifacts

```csharp
// Get model artifacts (grouped weight files)
string repoId = "thebloke/Llama-2-7B-GGUF";
var artifacts = await client.GetModelArtifactsAsync(repoId);

foreach (var artifact in artifacts)
{
    Console.WriteLine($"Artifact: {artifact.Name}");
    Console.WriteLine($"Format: {artifact.Format}");
    Console.WriteLine($"Total Size: {Common.StringFormatter.FormatSize(artifact.TotalSize)}");
    Console.WriteLine($"Files: {artifact.Files.Count}");
}
```

## Working with Multi-File Models

Some large models on Hugging Face Hub are split into multiple files or organized in subdirectories. The library provides specialized support for handling these complex repository structures.

### Handling Models with Subdirectories

```csharp
// Example for a model with files in subdirectories
string repoId = "unsloth/DeepSeek-R1-GGUF";
string outputDir = "path/to/output/directory";

// Get repository structure information
var model = await client.FindModelByRepoIdAsync(repoId);
var siblings = model.Siblings;

// Check if the model has files in subdirectories
bool hasSubdirectories = siblings.Any(s => s.Filename.Contains('/'));
Console.WriteLine($"Model has subdirectories: {hasSubdirectories}");

// Download entire repository with all subdirectories
await foreach (var progress in client.DownloadRepositoryAsync(repoId, outputDir))
{
    Console.WriteLine($"Total Progress: {progress.TotalProgress:P0}");
}
```

### Handling Split Files

```csharp
// Example for working with split files (e.g., "model-00001-of-00005.gguf")
string repoId = "unsloth/DeepSeek-R1-GGUF";
string outputDir = "path/to/output/directory";

// Get model artifacts which automatically groups split files
var artifacts = await client.GetModelArtifactsAsync(repoId);

// Find a specific quantization format artifact
var q4Artifact = artifacts.FirstOrDefault(a => a.Name.Contains("Q4_K_M"));
if (q4Artifact != null)
{
    Console.WriteLine($"Found artifact: {q4Artifact.Name}");
    Console.WriteLine($"Total size: {Common.StringFormatter.FormatSize(q4Artifact.TotalSize)}");
    Console.WriteLine($"Number of split files: {q4Artifact.Files.Count}");
    
    // Download all files for this specific artifact
    var filesToDownload = q4Artifact.Files.Select(f => f.Path).ToArray();
    await foreach (var progress in client.DownloadRepositoryFilesAsync(repoId, filesToDownload, outputDir))
    {
        Console.WriteLine($"Download Progress: {progress.TotalProgress:P0}");
    }
}
```

### Exploring Subdirectory Contents

```csharp
// Example for exploring files in a specific subdirectory
string repoId = "unsloth/DeepSeek-R1-GGUF";
string subDir = "DeepSeek-R1-Q4_K_M"; // Specific quantization version directory

// Get files in a specific subdirectory
var files = await client.GetRepositoryFilesAsync(repoId, subDir);
foreach (var file in files)
{
    if (file.TryGetProperty("path", out var path))
    {
        Console.WriteLine($"File: {path.GetString()}");
    }
    
    if (file.TryGetProperty("size", out var size))
    {
        Console.WriteLine($"Size: {Common.StringFormatter.FormatSize(size.GetInt64())}");
    }
}
```

### Analyzing Split File Patterns

```csharp
// Detect and analyze split file patterns in a model repository
string repoId = "unsloth/DeepSeek-R1-GGUF";
var model = await client.FindModelByRepoIdAsync(repoId);
var fileSizes = await client.GetRepositoryFileSizesAsync(repoId);
var artifacts = LMModelArtifactAnalyzer.GetModelArtifacts(model, fileSizes);

// Group artifacts by format
var artifactsByFormat = artifacts
    .GroupBy(a => a.Format)
    .OrderByDescending(g => g.Count());

foreach (var group in artifactsByFormat)
{
    Console.WriteLine($"Format: {group.Key}");
    Console.WriteLine($"Artifact count: {group.Count()}");
    
    foreach (var artifact in group.OrderByDescending(a => a.TotalSize))
    {
        Console.WriteLine($"  - {artifact.Name}: {Common.StringFormatter.FormatSize(artifact.TotalSize)}");
        Console.WriteLine($"    Split into {artifact.Files.Count} files");
    }
}
```

## Resulting File System Layout

When downloading a multi-file model with subdirectories, the file structure is preserved:

```
/output_directory
戍式式 unsloth_DeepSeek-R1-GGUF  // If useSubDir=true
弛   戍式式 DeepSeek-R1-Q4_K_M
弛   弛   戍式式 DeepSeek-R1-Q4_K_M-00001-of-00009.gguf
弛   弛   戍式式 DeepSeek-R1-Q4_K_M-00002-of-00009.gguf
弛   弛   戌式式 ...
弛   戌式式 config.json
```

## Configuration Options

The `HuggingFaceClientOptions` class provides the following configuration options:

- `Token`: API authentication token
- `MaxConcurrentDownloads` (1-20): Maximum number of concurrent downloads
- `ProgressUpdateInterval` (50-5000ms): Interval between progress updates
- `Timeout` (10s-30min): HTTP request timeout duration
- `BufferSize` (4KB-1MB): Buffer size for file downloads
- `MaxRetries` (0-5): Maximum number of retry attempts
- `RetryDelayMilliseconds` (100-10000ms): Base delay between retry attempts

## Error Handling

The library throws `HuggingFaceException` for API-related errors:

```csharp
try
{
    await client.FindModelByRepoIdAsync("invalid/repo");
}
catch (HuggingFaceException ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    Console.WriteLine($"Status Code: {ex.StatusCode}");
}
```

## Key Classes and Interfaces

- `IHuggingFaceClient`: Core interface for API interactions
- `HuggingFaceClient`: Main client implementation
- `HuggingFaceClientOptions`: Client configuration options
- `FileDownloadProgress`: File download progress information
- `RepoDownloadProgress`: Repository download progress information
- `HuggingFaceModel`: Represents model information
- `LMModelArtifact`: Represents a logical model artifact (grouped files)
- `ModelFileDownloadState`: Download state tracking information
- `StringFormatter`: Utility for formatting sizes, speeds, and progress

## Extension Methods

The library provides useful extension methods for model analysis:

```csharp
// Get all GGUF model file paths
var ggufFiles = model.GetGgufModelPaths();

// Check if a model has GGUF files
bool hasGguf = model.HasGgufFiles();

// Get essential model files (weights, configs, tokenizers)
var essentialFiles = model.GetEssentialModelPaths();
```