using LMSupplyDepots.SDK.ToolServing;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace LMSupplyDepots.SDK.ToolServing.Controllers;

/// <summary>
/// Tool serving HTTP API controller
/// </summary>
[ApiController]
[Route("v1/tools")]
public class ToolServingController : ControllerBase
{
    private readonly IToolRegistry _toolRegistry;
    private readonly IToolExecutionEngine _executionEngine;
    private readonly ILogger<ToolServingController> _logger;

    public ToolServingController(
        IToolRegistry toolRegistry,
        IToolExecutionEngine executionEngine,
        ILogger<ToolServingController> logger)
    {
        _toolRegistry = toolRegistry;
        _executionEngine = executionEngine;
        _logger = logger;
    }

    /// <summary>
    /// Discover available tools
    /// </summary>
    [HttpGet("discover")]
    public async Task<IActionResult> DiscoverTools([FromQuery] ToolFilter? filter = null)
    {
        try
        {
            var tools = await _toolRegistry.DiscoverToolsAsync(filter);
            return Ok(new
            {
                tools = tools.Select(t => new
                {
                    name = t.Name,
                    description = t.Description,
                    parameters = t.Definition.Parameters,
                    endpoint = t.Endpoint.Url,
                    method = t.Endpoint.Method,
                    capabilities = new
                    {
                        supports_streaming = t.Capabilities.SupportsStreaming,
                        supports_async = t.Capabilities.SupportsAsync,
                        requires_auth = t.Capabilities.RequiresAuth,
                        max_execution_time = t.Capabilities.MaxExecutionTime?.ToString()
                    },
                    metrics = new
                    {
                        execution_count = t.Metrics.ExecutionCount,
                        success_rate = t.Metrics.ExecutionCount > 0 ?
                            (double)t.Metrics.SuccessCount / t.Metrics.ExecutionCount : 0.0,
                        average_execution_time = t.Metrics.AverageExecutionTime.ToString(),
                        last_executed = t.Metrics.LastExecuted
                    }
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering tools");
            return StatusCode(500, new { error = "Internal server error", message = ex.Message });
        }
    }

    /// <summary>
    /// Get tool descriptor by name
    /// </summary>
    [HttpGet("{toolName}")]
    public async Task<IActionResult> GetTool(string toolName)
    {
        try
        {
            var tool = await _toolRegistry.GetToolDescriptorAsync(toolName);
            if (tool == null)
            {
                return NotFound(new { error = "Tool not found", tool_name = toolName });
            }

            return Ok(new
            {
                name = tool.Name,
                description = tool.Description,
                parameters = tool.Definition.Parameters,
                endpoint = tool.Endpoint.Url,
                method = tool.Endpoint.Method,
                capabilities = tool.Capabilities,
                metrics = tool.Metrics,
                metadata = tool.Metadata
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting tool: {ToolName}", toolName);
            return StatusCode(500, new { error = "Internal server error", message = ex.Message });
        }
    }

    /// <summary>
    /// Execute a tool
    /// </summary>
    [HttpPost("execute")]
    public async Task<IActionResult> ExecuteTool([FromBody] ToolExecutionRequest request)
    {
        if (request == null)
        {
            return BadRequest(new { error = "Request body is required" });
        }

        if (string.IsNullOrEmpty(request.ToolName))
        {
            return BadRequest(new { error = "tool_name is required" });
        }

        var requestId = request.RequestId ?? Guid.NewGuid().ToString();

        try
        {
            var context = new ToolExecutionContext
            {
                RequestId = requestId,
                ToolName = request.ToolName,
                ArgumentsJson = request.Arguments ?? "{}",
                Metadata = request.Metadata ?? new Dictionary<string, object>(),
                CancellationToken = HttpContext.RequestAborted,
                TraceId = request.TraceId,
                UserId = request.UserId
            };

            var result = await _executionEngine.ExecuteAsync(context);

            if (result.Success)
            {
                return Ok(new
                {
                    request_id = result.RequestId,
                    tool_name = result.ToolName,
                    success = result.Success,
                    result = result.Result != null ? JsonSerializer.Deserialize<object>(result.Result) : null,
                    execution_time = result.ExecutionTime.ToString(),
                    metadata = result.Metadata
                });
            }
            else
            {
                return BadRequest(new
                {
                    request_id = result.RequestId,
                    tool_name = result.ToolName,
                    success = result.Success,
                    error = result.ErrorMessage,
                    execution_time = result.ExecutionTime.ToString()
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing tool: {ToolName}", request.ToolName);
            return StatusCode(500, new
            {
                request_id = requestId,
                tool_name = request.ToolName,
                success = false,
                error = "Internal server error",
                message = ex.Message
            });
        }
    }

    /// <summary>
    /// Execute a tool with streaming response
    /// </summary>
    [HttpPost("execute/stream")]
    public async Task<IActionResult> ExecuteToolStream([FromBody] ToolExecutionRequest request)
    {
        if (request == null)
        {
            return BadRequest(new { error = "Request body is required" });
        }

        if (string.IsNullOrEmpty(request.ToolName))
        {
            return BadRequest(new { error = "tool_name is required" });
        }

        var requestId = request.RequestId ?? Guid.NewGuid().ToString();

        try
        {
            var context = new ToolExecutionContext
            {
                RequestId = requestId,
                ToolName = request.ToolName,
                ArgumentsJson = request.Arguments ?? "{}",
                Metadata = request.Metadata ?? new Dictionary<string, object>(),
                CancellationToken = HttpContext.RequestAborted,
                TraceId = request.TraceId,
                UserId = request.UserId
            };

            Response.Headers.Add("Content-Type", "text/plain; charset=utf-8");
            Response.Headers.Add("Cache-Control", "no-cache");
            Response.Headers.Add("Connection", "keep-alive");

            var events = await _executionEngine.ExecuteStreamAsync(context);

            await foreach (var @event in events)
            {
                var eventJson = JsonSerializer.Serialize(new
                {
                    request_id = @event.RequestId,
                    event_type = @event.EventType,
                    data = @event.Data,
                    timestamp = @event.Timestamp
                });

                await Response.WriteAsync($"data: {eventJson}\n\n");
                await Response.Body.FlushAsync();
            }

            return new EmptyResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing tool stream: {ToolName}", request.ToolName);

            var errorEventJson = JsonSerializer.Serialize(new
            {
                request_id = requestId,
                event_type = "error",
                data = ex.Message,
                timestamp = DateTime.UtcNow
            });

            await Response.WriteAsync($"data: {errorEventJson}\n\n");
            await Response.Body.FlushAsync();

            return new EmptyResult();
        }
    }

    /// <summary>
    /// Execute multiple tools in batch
    /// </summary>
    [HttpPost("batch")]
    public async Task<IActionResult> ExecuteToolsBatch([FromBody] ToolBatchExecutionRequest request)
    {
        if (request?.Requests == null || !request.Requests.Any())
        {
            return BadRequest(new { error = "At least one tool request is required" });
        }

        var batchId = Guid.NewGuid().ToString();
        var results = new List<object>();

        foreach (var toolRequest in request.Requests)
        {
            var requestId = toolRequest.RequestId ?? Guid.NewGuid().ToString();

            try
            {
                var context = new ToolExecutionContext
                {
                    RequestId = requestId,
                    ToolName = toolRequest.ToolName,
                    ArgumentsJson = toolRequest.Arguments ?? "{}",
                    Metadata = toolRequest.Metadata ?? new Dictionary<string, object>(),
                    CancellationToken = HttpContext.RequestAborted,
                    TraceId = toolRequest.TraceId,
                    UserId = toolRequest.UserId
                };

                var result = await _executionEngine.ExecuteAsync(context);

                results.Add(new
                {
                    request_id = result.RequestId,
                    tool_name = result.ToolName,
                    success = result.Success,
                    result = result.Success && result.Result != null ?
                        JsonSerializer.Deserialize<object>(result.Result) : null,
                    error = result.ErrorMessage,
                    execution_time = result.ExecutionTime.ToString()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing tool in batch: {ToolName}", toolRequest.ToolName);

                results.Add(new
                {
                    request_id = requestId,
                    tool_name = toolRequest.ToolName,
                    success = false,
                    error = "Internal server error",
                    message = ex.Message
                });
            }
        }

        return Ok(new
        {
            batch_id = batchId,
            results = results,
            summary = new
            {
                total = results.Count,
                successful = results.Count(r => (bool)((dynamic)r).success),
                failed = results.Count(r => !(bool)((dynamic)r).success)
            }
        });
    }
}

/// <summary>
/// Tool execution request model
/// </summary>
public class ToolExecutionRequest
{
    public string RequestId { get; set; } = string.Empty;
    public string ToolName { get; set; } = string.Empty;
    public string? Arguments { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
    public string? TraceId { get; set; }
    public string? UserId { get; set; }
}

/// <summary>
/// Tool batch execution request model
/// </summary>
public class ToolBatchExecutionRequest
{
    public List<ToolExecutionRequest> Requests { get; set; } = new();
    public bool ContinueOnError { get; set; } = true;
    public int MaxConcurrency { get; set; } = 5;
}
