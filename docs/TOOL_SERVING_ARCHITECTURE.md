# LMSupplyDepots Tool Serving Architecture

## Overview

This document outlines the proposed tool serving architecture for LMSupplyDepots to support both direct tool invocation and external LLM orchestration frameworks.

## Current Architecture vs Proposed Enhancement

### Current: Direct Invocation Only
```
Client → Host API → SDK → ToolService → IFunctionTool → Execute
```

### Proposed: Multi-Layer Tool Serving
```
External Orchestrators (YoMo, LangChain, etc.)
                ↓
        Tool Serving Layer (HTTP/gRPC API)
                ↓
        Tool Registry & Discovery
                ↓
        Tool Execution Engine
                ↓
        Function Tools Implementation
```

## Proposed Components

### 1. Tool Serving Layer

#### HTTP Tool Server
Expose tools as HTTP endpoints compatible with OpenAI Functions format:

```http
POST /v1/tools/execute
Content-Type: application/json

{
  "tool_name": "get_weather",
  "arguments": {"location": "Seoul, Korea"},
  "request_id": "uuid-here"
}
```

#### Tool Discovery Endpoint
```http
GET /v1/tools/discover
```

Response:
```json
{
  "tools": [
    {
      "name": "get_weather",
      "description": "Get current weather for a location",
      "parameters": {...},
      "endpoint": "/v1/tools/execute",
      "method": "POST"
    }
  ]
}
```

### 2. Tool Registry

Centralized registry for tool metadata and capabilities:

```csharp
public interface IToolRegistry
{
    Task RegisterToolAsync(IFunctionTool tool, ToolMetadata metadata);
    Task<IReadOnlyList<ToolDescriptor>> DiscoverToolsAsync(ToolFilter? filter = null);
    Task<ToolDescriptor?> GetToolDescriptorAsync(string toolName);
    Task UnregisterToolAsync(string toolName);
}

public class ToolDescriptor
{
    public string Name { get; set; }
    public string Description { get; set; }
    public FunctionDefinition Definition { get; set; }
    public ToolCapabilities Capabilities { get; set; }
    public ToolEndpoint Endpoint { get; set; }
    public ToolMetrics Metrics { get; set; }
}
```

### 3. Tool Orchestration Bridge

Support for external orchestration frameworks:

```csharp
public interface IToolOrchestrationBridge
{
    // YoMo-style serverless function registration
    Task<string> RegisterServerlessFunctionAsync(string functionName, IFunctionTool tool);
    
    // LangChain-style tool binding
    Task<IToolBinding> CreateToolBindingAsync(IEnumerable<IFunctionTool> tools);
    
    // LlamaIndex-style agent integration
    Task<IAgentContext> CreateAgentContextAsync(IEnumerable<IFunctionTool> tools);
}
```

### 4. Tool Execution Context

Enhanced execution context with tracing and metrics:

```csharp
public class ToolExecutionContext
{
    public string RequestId { get; set; }
    public string ToolName { get; set; }
    public string ArgumentsJson { get; set; }
    public Dictionary<string, object> Metadata { get; set; }
    public CancellationToken CancellationToken { get; set; }
    public ITraceContext? TraceContext { get; set; }
}

public interface IToolExecutionEngine
{
    Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context);
    Task<IAsyncEnumerable<ToolExecutionEvent>> ExecuteStreamAsync(ToolExecutionContext context);
}
```

## Integration Patterns

### 1. YoMo Integration

```csharp
// YoMo-style function registration
public class YoMoToolBridge : IToolOrchestrationBridge
{
    public async Task<string> RegisterServerlessFunctionAsync(string functionName, IFunctionTool tool)
    {
        var endpoint = $"/sfn/{functionName}";
        await _toolRegistry.RegisterToolAsync(tool, new ToolMetadata
        {
            Protocol = "yomo-sfn",
            Endpoint = endpoint,
            Runtime = "csharp"
        });
        
        return endpoint;
    }
}
```

### 2. OpenAI Functions Compatibility

```csharp
// OpenAI Functions format support
public class OpenAIToolAdapter
{
    public OpenAIFunction ToOpenAIFunction(IFunctionTool tool)
    {
        return new OpenAIFunction
        {
            Name = tool.Definition.Name,
            Description = tool.Definition.Description,
            Parameters = tool.Definition.Parameters
        };
    }
}
```

### 3. Model Context Protocol (MCP) Support

```csharp
// MCP server integration
public class MCPToolServer : IMCPServer
{
    public async Task<MCPToolList> ListToolsAsync()
    {
        var tools = await _toolRegistry.DiscoverToolsAsync();
        return new MCPToolList
        {
            Tools = tools.Select(t => new MCPTool
            {
                Name = t.Name,
                Description = t.Description,
                InputSchema = t.Definition.Parameters
            }).ToList()
        };
    }
}
```

## Configuration

### Tool Server Configuration

```json
{
  "ToolServing": {
    "EnableHttpServer": true,
    "HttpPort": 8080,
    "EnableGrpcServer": false,
    "GrpcPort": 8081,
    "EnableDiscovery": true,
    "EnableMetrics": true,
    "EnableTracing": true,
    "MaxConcurrentExecutions": 100,
    "ExecutionTimeout": "00:05:00",
    "Bridges": {
      "YoMo": {
        "Enabled": true,
        "ZipperEndpoint": "localhost:9000"
      },
      "MCP": {
        "Enabled": true,
        "ServerName": "lmsupplydepots-tools"
      }
    }
  }
}
```

## Benefits

### For Direct Usage
- Existing functionality preserved
- Enhanced monitoring and metrics
- Better error handling and tracing

### For External Orchestration
- **YoMo**: Function-as-a-Service deployment
- **LangChain**: Tool binding and agent workflows
- **LlamaIndex**: Agent system integration
- **OpenAI**: Direct function calling compatibility
- **MCP**: Model Context Protocol support

### For Platform Operators
- Centralized tool management
- Usage analytics and monitoring
- Security and access control
- Scalability and load balancing

## Implementation Phases

### Phase 1: Tool Registry & Discovery
- Implement IToolRegistry interface
- Add tool discovery HTTP endpoints
- Tool metadata management

### Phase 2: Enhanced Execution Engine
- Execution context and tracing
- Metrics collection
- Error handling improvements

### Phase 3: External Bridge Support
- YoMo bridge implementation
- OpenAI Functions adapter
- MCP server support

### Phase 4: Advanced Features
- Tool versioning
- A/B testing for tools
- Tool composition and chaining
- Security and authentication

## Compatibility Matrix

| Framework | Direct Call | HTTP API | Bridge Support | Status |
|-----------|-------------|----------|----------------|--------|
| Direct SDK | ✅ | ✅ | N/A | Current |
| OpenAI Functions | ✅ | ✅ | ✅ | Planned |
| YoMo SFN | ❌ | ✅ | ✅ | Planned |
| LangChain | ❌ | ✅ | ✅ | Planned |
| LlamaIndex | ❌ | ✅ | ✅ | Planned |
| MCP | ❌ | ✅ | ✅ | Planned |

## Conclusion

This architecture transforms LMSupplyDepots from a local model server into a comprehensive tool serving platform that can integrate with any LLM orchestration framework while maintaining backward compatibility.
