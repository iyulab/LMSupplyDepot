# LMSupplyDepots.External.LLamaEngine

## Overview
LMSupplyDepots.External.LLamaEngine is a .NET library for managing and running LLaMA-based language models in local environments. It provides a comprehensive wrapper around LLamaSharp, offering model loading, hardware backend detection (CUDA, Vulkan, CPU), inference operations, and resource management.

## Features

### Model Management
- Efficient model loading and unloading
- State tracking for local models
- Support for model configuration via JSON
- Normalized model identifiers

### Hardware Optimization
- Automatic detection of hardware acceleration capabilities:
  - CUDA 11/12
  - Vulkan
  - CPU fallback
- Dynamic parameter tuning based on available hardware
- Optimal context size determination based on system memory
- GPU layer optimization based on available VRAM

### Inference Capabilities
- Complete text generation with customizable parameters
- Streaming inference for real-time responses
- Text embedding generation with normalization
- Chat history management with template support

### Resource Management
- Efficient memory usage with automatic cleanup
- Proper resource disposal
- Error handling with retry mechanisms
- Thread-safe operations

### Monitoring
- Real-time system metrics monitoring:
  - CPU usage
  - Memory consumption
  - GPU utilization, memory usage, and temperature
  - Disk space and usage

### Integration
- Dependency Injection support for easy application integration
- Event-based notification system
- Asynchronous API design

## Getting Started

### Installation
Add the package to your project:

```bash
dotnet add package LMSupplyDepots.External.LLamaEngine
```

### Register Services
In your application startup:

```csharp
using LMSupplyDepots.External.LLamaEngine;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddLLamaEngine();

// Build service provider
var serviceProvider = services.BuildServiceProvider();
```

### Basic Usage
Loading and using a model:

```csharp
using LMSupplyDepots.External.LLamaEngine.Services;

// Get services
var modelManager = serviceProvider.GetRequiredService<ILLamaModelManager>();
var llmService = serviceProvider.GetRequiredService<ILLMService>();

// Load model
string modelPath = "/path/to/model.gguf";
string modelId = "llama2/7b/model";
await modelManager.LoadModelAsync(modelPath, modelId);

// Generate text
string prompt = "Explain quantum computing in simple terms";
string response = await llmService.InferAsync(modelId, prompt);
Console.WriteLine(response);

// Stream responses
await foreach (var chunk in llmService.InferStreamAsync(modelId, prompt))
{
    Console.Write(chunk);
}

// Create embeddings
float[] embedding = await llmService.CreateEmbeddingAsync(modelId, "Hello world");

// Unload model when done
await modelManager.UnloadModelAsync(modelId);
```

### Chat Conversations

```csharp
using LMSupplyDepots.External.LLamaEngine.Chat;

// Create chat history
var chatHistory = new ChatHistory(
    systemPrompt: "You are a helpful AI assistant.",
    maxHistoryLength: 10
);

// Add messages
chatHistory.AddMessage("user", "Hello, who are you?");

// Format for the model
string prompt = chatHistory.GetFormattedPrompt();

// Get response
string response = await llmService.InferAsync(modelId, prompt);

// Add response to history
chatHistory.AddMessage("assistant", response);
```

### System Monitoring

```csharp
using LMSupplyDepots.External.LLamaEngine.Services;

var monitorService = serviceProvider.GetRequiredService<ISystemMonitorService>();

// Get current metrics
SystemMetrics metrics = monitorService.GetCurrentMetrics();
Console.WriteLine($"CPU: {metrics.CpuUsagePercent}%, Memory: {metrics.MemoryUsagePercent}%");

// Continuous monitoring
await foreach (var snapshot in monitorService.MonitorResourcesAsync(
    interval: TimeSpan.FromSeconds(5),
    cancellationToken: cts.Token))
{
    Console.WriteLine($"CPU: {snapshot.CpuUsagePercent}%, " +
                     $"Memory: {snapshot.MemoryUsagePercent}%");
    
    foreach (var gpu in snapshot.GpuMetrics)
    {
        Console.WriteLine($"GPU {gpu.Key}: {gpu.Value.UtilizationPercent}%, " +
                         $"Temp: {gpu.Value.TemperatureCelsius}¡ÆC");
    }
}
```

## Dependencies
- LLamaSharp
- LLamaSharp.Backend.Cpu
- LLamaSharp.Backend.Cuda11
- LLamaSharp.Backend.Cuda12
- LLamaSharp.Backend.Vulkan

## System Requirements
- .NET 8.0 or later
- For GPU acceleration:
  - CUDA: NVIDIA GPU with CUDA 11.x or 12.x and compatible drivers
  - Vulkan: GPU with Vulkan 1.2+ support and compatible drivers
- Sufficient RAM for model loading (varies by model size)