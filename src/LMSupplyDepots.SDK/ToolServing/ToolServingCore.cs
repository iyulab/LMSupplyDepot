using LMSupplyDepots.SDK.OpenAI.Models;
using LMSupplyDepots.SDK.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Text.Json;

namespace LMSupplyDepots.SDK.ToolServing;

/// <summary>
/// Tool serving configuration options
/// </summary>
public class ToolServingOptions
{
    public bool EnableHttpServer { get; set; } = true;
    public int HttpPort { get; set; } = 8080;
    public bool EnableGrpcServer { get; set; } = false;
    public int GrpcPort { get; set; } = 8081;
    public bool EnableDiscovery { get; set; } = true;
    public bool EnableMetrics { get; set; } = true;
    public bool EnableTracing { get; set; } = true;
    public int MaxConcurrentExecutions { get; set; } = 100;
    public TimeSpan ExecutionTimeout { get; set; } = TimeSpan.FromMinutes(5);
    public BridgeOptions Bridges { get; set; } = new();
}

public class BridgeOptions
{
    public YoMoBridgeOptions YoMo { get; set; } = new();
    public MCPBridgeOptions MCP { get; set; } = new();
}

public class YoMoBridgeOptions
{
    public bool Enabled { get; set; } = false;
    public string ZipperEndpoint { get; set; } = "localhost:9000";
}

public class MCPBridgeOptions
{
    public bool Enabled { get; set; } = false;
    public string ServerName { get; set; } = "lmsupplydepots-tools";
}

/// <summary>
/// Tool metadata for registry
/// </summary>
public class ToolMetadata
{
    public required string Name { get; set; }
    public required string Description { get; set; }
    public string? Version { get; set; }
    public string? Author { get; set; }
    public List<string> Tags { get; set; } = new();
    public string Protocol { get; set; } = "direct";
    public string? Endpoint { get; set; }
    public string Runtime { get; set; } = "csharp";
    public Dictionary<string, object> Properties { get; set; } = new();
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Tool capabilities descriptor
/// </summary>
public class ToolCapabilities
{
    public bool SupportsStreaming { get; set; } = false;
    public bool SupportsAsync { get; set; } = true;
    public bool RequiresAuth { get; set; } = false;
    public TimeSpan? MaxExecutionTime { get; set; }
    public List<string> RequiredPermissions { get; set; } = new();
}

/// <summary>
/// Tool endpoint information
/// </summary>
public class ToolEndpoint
{
    public string Url { get; set; } = string.Empty;
    public string Method { get; set; } = "POST";
    public Dictionary<string, string> Headers { get; set; } = new();
    public string ContentType { get; set; } = "application/json";
}

/// <summary>
/// Tool execution metrics
/// </summary>
public class ToolMetrics
{
    public int ExecutionCount { get; set; }
    public int SuccessCount { get; set; }
    public int ErrorCount { get; set; }
    public TimeSpan AverageExecutionTime { get; set; }
    public DateTime LastExecuted { get; set; }
    public List<string> RecentErrors { get; set; } = new();
}

/// <summary>
/// Comprehensive tool descriptor
/// </summary>
public class ToolDescriptor
{
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required FunctionDefinition Definition { get; set; }
    public ToolCapabilities Capabilities { get; set; } = new();
    public ToolEndpoint Endpoint { get; set; } = new();
    public ToolMetrics Metrics { get; set; } = new();
    public ToolMetadata Metadata { get; set; } = new() { Name = "", Description = "" };
}

/// <summary>
/// Tool filter for discovery
/// </summary>
public class ToolFilter
{
    public string? Name { get; set; }
    public List<string>? Tags { get; set; }
    public string? Protocol { get; set; }
    public bool? RequiresAuth { get; set; }
    public bool? SupportsStreaming { get; set; }
}

/// <summary>
/// Tool execution context
/// </summary>
public class ToolExecutionContext
{
    public required string RequestId { get; set; }
    public required string ToolName { get; set; }
    public required string ArgumentsJson { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
    public CancellationToken CancellationToken { get; set; } = default;
    public string? TraceId { get; set; }
    public string? UserId { get; set; }
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Tool execution result
/// </summary>
public class ToolExecutionResult
{
    public required string RequestId { get; set; }
    public required string ToolName { get; set; }
    public bool Success { get; set; }
    public string? Result { get; set; }
    public string? ErrorMessage { get; set; }
    public TimeSpan ExecutionTime { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Tool execution event for streaming
/// </summary>
public class ToolExecutionEvent
{
    public required string RequestId { get; set; }
    public required string EventType { get; set; } // started, progress, completed, error
    public string? Data { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Tool registry interface
/// </summary>
public interface IToolRegistry
{
    Task RegisterToolAsync(IFunctionTool tool, ToolMetadata metadata);
    Task<IReadOnlyList<ToolDescriptor>> DiscoverToolsAsync(ToolFilter? filter = null);
    Task<ToolDescriptor?> GetToolDescriptorAsync(string toolName);
    Task UnregisterToolAsync(string toolName);
    Task UpdateToolMetricsAsync(string toolName, ToolExecutionResult result);
}

/// <summary>
/// Tool execution engine interface
/// </summary>
public interface IToolExecutionEngine
{
    Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context);
    Task<IAsyncEnumerable<ToolExecutionEvent>> ExecuteStreamAsync(ToolExecutionContext context);
}

/// <summary>
/// Tool orchestration bridge interface
/// </summary>
public interface IToolOrchestrationBridge
{
    Task<string> RegisterServerlessFunctionAsync(string functionName, IFunctionTool tool);
    Task<IToolBinding> CreateToolBindingAsync(IEnumerable<IFunctionTool> tools);
    Task<IAgentContext> CreateAgentContextAsync(IEnumerable<IFunctionTool> tools);
}

/// <summary>
/// Tool binding interface for LangChain-style integration
/// </summary>
public interface IToolBinding
{
    IReadOnlyList<IFunctionTool> Tools { get; }
    Task<object> InvokeAsync(string input, CancellationToken cancellationToken = default);
}

/// <summary>
/// Agent context interface for LlamaIndex-style integration
/// </summary>
public interface IAgentContext
{
    IReadOnlyList<IFunctionTool> AvailableTools { get; }
    Task<string> RunAsync(string query, CancellationToken cancellationToken = default);
}

/// <summary>
/// In-memory tool registry implementation
/// </summary>
public class InMemoryToolRegistry : IToolRegistry
{
    private readonly ConcurrentDictionary<string, (IFunctionTool Tool, ToolDescriptor Descriptor)> _tools = new();
    private readonly ILogger<InMemoryToolRegistry> _logger;

    public InMemoryToolRegistry(ILogger<InMemoryToolRegistry> logger)
    {
        _logger = logger;
    }

    public Task RegisterToolAsync(IFunctionTool tool, ToolMetadata metadata)
    {
        var descriptor = new ToolDescriptor
        {
            Name = tool.Definition.Name,
            Description = tool.Definition.Description ?? metadata.Description,
            Definition = tool.Definition,
            Metadata = metadata,
            Endpoint = new ToolEndpoint
            {
                Url = $"/v1/tools/execute",
                Method = "POST"
            }
        };

        _tools[tool.Definition.Name] = (tool, descriptor);
        _logger.LogInformation("Registered tool: {ToolName}", tool.Definition.Name);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ToolDescriptor>> DiscoverToolsAsync(ToolFilter? filter = null)
    {
        var tools = _tools.Values.Select(t => t.Descriptor).AsEnumerable();

        if (filter != null)
        {
            if (!string.IsNullOrEmpty(filter.Name))
            {
                tools = tools.Where(t => t.Name.Contains(filter.Name, StringComparison.OrdinalIgnoreCase));
            }

            if (filter.Tags?.Any() == true)
            {
                tools = tools.Where(t => filter.Tags.Any(tag => t.Metadata.Tags.Contains(tag)));
            }

            if (!string.IsNullOrEmpty(filter.Protocol))
            {
                tools = tools.Where(t => t.Metadata.Protocol == filter.Protocol);
            }

            if (filter.RequiresAuth.HasValue)
            {
                tools = tools.Where(t => t.Capabilities.RequiresAuth == filter.RequiresAuth.Value);
            }

            if (filter.SupportsStreaming.HasValue)
            {
                tools = tools.Where(t => t.Capabilities.SupportsStreaming == filter.SupportsStreaming.Value);
            }
        }

        var result = tools.ToList().AsReadOnly();
        return Task.FromResult<IReadOnlyList<ToolDescriptor>>(result);
    }

    public Task<ToolDescriptor?> GetToolDescriptorAsync(string toolName)
    {
        _tools.TryGetValue(toolName, out var tool);
        return Task.FromResult<ToolDescriptor?>(tool.Descriptor);
    }

    public Task UnregisterToolAsync(string toolName)
    {
        if (_tools.TryRemove(toolName, out _))
        {
            _logger.LogInformation("Unregistered tool: {ToolName}", toolName);
        }
        return Task.CompletedTask;
    }

    public Task UpdateToolMetricsAsync(string toolName, ToolExecutionResult result)
    {
        if (_tools.TryGetValue(toolName, out var tool))
        {
            var metrics = tool.Descriptor.Metrics;
            metrics.ExecutionCount++;
            metrics.LastExecuted = DateTime.UtcNow;

            if (result.Success)
            {
                metrics.SuccessCount++;
            }
            else
            {
                metrics.ErrorCount++;
                if (!string.IsNullOrEmpty(result.ErrorMessage))
                {
                    metrics.RecentErrors.Add($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}: {result.ErrorMessage}");
                    // Keep only recent 10 errors
                    if (metrics.RecentErrors.Count > 10)
                    {
                        metrics.RecentErrors.RemoveAt(0);
                    }
                }
            }

            // Update average execution time
            var totalTime = metrics.AverageExecutionTime.TotalMilliseconds * (metrics.ExecutionCount - 1) + result.ExecutionTime.TotalMilliseconds;
            metrics.AverageExecutionTime = TimeSpan.FromMilliseconds(totalTime / metrics.ExecutionCount);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Enhanced tool execution engine
/// </summary>
public class ToolExecutionEngine : IToolExecutionEngine
{
    private readonly IToolRegistry _toolRegistry;
    private readonly IToolService _toolService;
    private readonly ILogger<ToolExecutionEngine> _logger;
    private readonly ToolServingOptions _options;

    public ToolExecutionEngine(
        IToolRegistry toolRegistry,
        IToolService toolService,
        ILogger<ToolExecutionEngine> logger,
        IOptions<ToolServingOptions> options)
    {
        _toolRegistry = toolRegistry;
        _toolService = toolService;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context)
    {
        var startTime = DateTime.UtcNow;
        var result = new ToolExecutionResult
        {
            RequestId = context.RequestId,
            ToolName = context.ToolName
        };

        try
        {
            _logger.LogInformation("Executing tool: {ToolName} with request ID: {RequestId}",
                context.ToolName, context.RequestId);

            // Execute with timeout
            using var timeoutCts = new CancellationTokenSource(_options.ExecutionTimeout);
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
                context.CancellationToken, timeoutCts.Token);

            var toolResult = await _toolService.ExecuteToolAsync(
                context.ToolName,
                context.ArgumentsJson,
                combinedCts.Token);

            result.Success = true;
            result.Result = toolResult;
            result.ExecutionTime = DateTime.UtcNow - startTime;

            _logger.LogInformation("Tool execution completed: {ToolName} in {ExecutionTime}ms",
                context.ToolName, result.ExecutionTime.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.ExecutionTime = DateTime.UtcNow - startTime;

            _logger.LogError(ex, "Tool execution failed: {ToolName} with request ID: {RequestId}",
                context.ToolName, context.RequestId);
        }

        // Update metrics
        await _toolRegistry.UpdateToolMetricsAsync(context.ToolName, result);

        return result;
    }

    public Task<IAsyncEnumerable<ToolExecutionEvent>> ExecuteStreamAsync(ToolExecutionContext context)
    {
        // For now, convert regular execution to streaming events
        // In the future, this could support tools that produce streaming output
        return Task.FromResult(ExecuteAsStreamingEvents(context));
    }

    private async IAsyncEnumerable<ToolExecutionEvent> ExecuteAsStreamingEvents(ToolExecutionContext context)
    {
        yield return new ToolExecutionEvent
        {
            RequestId = context.RequestId,
            EventType = "started",
            Data = JsonSerializer.Serialize(new { tool = context.ToolName, arguments = context.ArgumentsJson })
        };

        ToolExecutionResult result;
        Exception? executionException = null;

        try
        {
            result = await ExecuteAsync(context);
        }
        catch (Exception ex)
        {
            executionException = ex;
            result = new ToolExecutionResult
            {
                RequestId = context.RequestId,
                ToolName = context.ToolName,
                Success = false,
                ErrorMessage = ex.Message
            };
        }

        if (executionException != null)
        {
            yield return new ToolExecutionEvent
            {
                RequestId = context.RequestId,
                EventType = "error",
                Data = executionException.Message
            };
        }
        else if (result.Success)
        {
            yield return new ToolExecutionEvent
            {
                RequestId = context.RequestId,
                EventType = "completed",
                Data = result.Result
            };
        }
        else
        {
            yield return new ToolExecutionEvent
            {
                RequestId = context.RequestId,
                EventType = "error",
                Data = result.ErrorMessage
            };
        }
    }
}
