# LMSupplyDepots.External.OpenAI

A .NET library for integrating with OpenAI services, providing a clean and efficient interface for working with OpenAI's REST APIs.

## Overview

This library offers a streamlined approach to interact with OpenAI services, handling file operations, vector stores, chat completions, and RAG (Retrieval Augmented Generation) queries.

## Features

- **File Management**: Upload, list, and delete files on OpenAI's platform
- **Vector Store Operations**: Create and manage vector stores for efficient document retrieval
- **Chat Interface**: Send messages and manage conversations with OpenAI's models
- **RAG Queries**: Perform Retrieval Augmented Generation queries on your documents

## Installation

Install the package via NuGet:

```bash
dotnet add package LMSupplyDepots.External.OpenAI
```

## Quick Start

```csharp
using LMSupplyDepots.External.OpenAI;

// Initialize the service with your API key
var openAI = new OpenAIService("your-api-key");

// Send a simple chat message
string response = await openAI.Chat.SendMessageAsync(
    "What is retrieval augmented generation?", 
    "You are a helpful assistant."
);
Console.WriteLine(response);
```

## Usage Examples

### File Operations

```csharp
// Upload files
var file = await openAI.File.UploadFileAsync("path/to/your/document.pdf");
Console.WriteLine($"File uploaded with ID: {file.Id}");

// List all files
var files = await openAI.File.ListFilesAsync();
foreach (var f in files)
{
    Console.WriteLine($"File: {f.Filename}, ID: {f.Id}");
}
```

### Creating Vector Stores

```csharp
// Create a vector store from uploaded files
var fileIds = new List<string> { "file-abc123", "file-def456" };
var vectorStore = await openAI.VectorStore.CreateVectorStoreAsync(fileIds, "my-documents");

// Wait for processing to complete
vectorStore = await openAI.VectorStore.WaitForVectorStoreProcessingAsync(vectorStore.Id);
```

### RAG Queries

```csharp
// Query documents using a vector store
var queryResponse = await openAI.Query.QueryFilesAsync(
    vectorStoreId: "vs-abc123",
    query: "What does the document say about performance optimization?"
);

// Extract the text from the response
string responseText = queryResponse.GetOutputText();
Console.WriteLine(responseText);

// Extract file citations
var citations = queryResponse.FileAnnotations();
foreach (var citation in citations)
{
    Console.WriteLine($"Citation: {citation}");
}
```

### Chat Conversations

```csharp
// Create a conversation
var messages = new List<(string role, string content)>
{
    ("system", "You are a helpful assistant."),
    ("user", "Hello, can you explain how vector embeddings work?"),
    ("assistant", "Vector embeddings are numerical representations of words or phrases..."),
    ("user", "How are they created?")
};

// Send the conversation
string response = await openAI.Chat.SendConversationAsync(messages);
```

### Streaming Responses

```csharp
// Stream a response with real-time updates
await openAI.Chat.StreamMessageAsync(
    "Write a short poem about artificial intelligence.",
    onUpdate: chunk => Console.Write(chunk)
);
```

## Advanced Configuration

The library supports custom model selection for different operations:

```csharp
// Initialize with custom models
var openAI = new OpenAIService(
    apiKey: "your-api-key",
    queryModel: "gpt-4o-mini",   // More efficient model for RAG queries
    chatModel: "gpt-4o"          // More capable model for chat
);
```

## Error Handling

The library includes comprehensive error handling and logging:

```csharp
try
{
    var response = await openAI.Chat.SendMessageAsync("Your query here");
    // Process response
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    // Handle exception appropriately
}
```

## Requirements

- .NET 8.0 or higher
- Valid OpenAI API key