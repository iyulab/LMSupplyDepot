using LMSupplyDepots.SDK.OpenAI.Models;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace LMSupplyDepots.SDK.Tools;

/// <summary>
/// Interface for function tools that can be called by the model
/// </summary>
public interface IFunctionTool
{
    /// <summary>
    /// The function definition for OpenAI compatibility
    /// </summary>
    FunctionDefinition Definition { get; }

    /// <summary>
    /// Execute the function with the provided arguments
    /// </summary>
    Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default);
}

/// <summary>
/// Service for managing and executing function tools
/// </summary>
public interface IToolService
{
    /// <summary>
    /// Register a function tool
    /// </summary>
    void RegisterTool(IFunctionTool tool);

    /// <summary>
    /// Get all registered tools as OpenAI tool definitions
    /// </summary>
    List<Tool> GetAvailableTools();

    /// <summary>
    /// Execute a tool call
    /// </summary>
    Task<string> ExecuteToolAsync(string toolName, string argumentsJson, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a tool is available
    /// </summary>
    bool IsToolAvailable(string toolName);
}

/// <summary>
/// Implementation of tool service
/// </summary>
public class ToolService : IToolService
{
    private readonly Dictionary<string, IFunctionTool> _tools = new();
    private readonly ILogger<ToolService> _logger;

    public ToolService(ILogger<ToolService> logger)
    {
        _logger = logger;
    }

    public void RegisterTool(IFunctionTool tool)
    {
        if (tool?.Definition?.Name == null)
        {
            throw new ArgumentException("Tool must have a valid definition with a name", nameof(tool));
        }

        _tools[tool.Definition.Name] = tool;
        _logger.LogInformation("Registered tool: {ToolName}", tool.Definition.Name);
    }

    public List<Tool> GetAvailableTools()
    {
        return _tools.Values.Select(tool => new Tool
        {
            Type = "function",
            Function = tool.Definition
        }).ToList();
    }

    public async Task<string> ExecuteToolAsync(string toolName, string argumentsJson, CancellationToken cancellationToken = default)
    {
        if (!_tools.TryGetValue(toolName, out var tool))
        {
            throw new ArgumentException($"Tool '{toolName}' not found", nameof(toolName));
        }

        try
        {
            _logger.LogInformation("Executing tool: {ToolName} with arguments: {Arguments}", toolName, argumentsJson);
            var result = await tool.ExecuteAsync(argumentsJson, cancellationToken);
            _logger.LogInformation("Tool execution completed: {ToolName}", toolName);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing tool: {ToolName}", toolName);
            throw;
        }
    }

    public bool IsToolAvailable(string toolName)
    {
        return _tools.ContainsKey(toolName);
    }
}

/// <summary>
/// Base class for implementing function tools
/// </summary>
public abstract class FunctionToolBase : IFunctionTool
{
    public abstract FunctionDefinition Definition { get; }

    public abstract Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default);

    /// <summary>
    /// Helper method to deserialize arguments
    /// </summary>
    protected T? DeserializeArguments<T>(string argumentsJson)
    {
        if (string.IsNullOrEmpty(argumentsJson))
            return default;

        try
        {
            return JsonSerializer.Deserialize<T>(argumentsJson);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"Invalid JSON arguments: {ex.Message}", nameof(argumentsJson), ex);
        }
    }
}

/// <summary>
/// Example weather tool implementation
/// </summary>
public class GetWeatherTool : FunctionToolBase
{
    public override FunctionDefinition Definition => new()
    {
        Name = "get_weather",
        Description = "Get the current weather for a specific location",
        Parameters = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["location"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "The city and state, e.g. San Francisco, CA"
                },
                ["unit"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["enum"] = new[] { "celsius", "fahrenheit" },
                    ["description"] = "The temperature unit to use"
                }
            },
            ["required"] = new[] { "location" }
        }
    };

    public override Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var args = DeserializeArguments<WeatherArgs>(argumentsJson);
        if (args?.Location == null)
        {
            return Task.FromResult("Error: Location parameter is required");
        }

        // Simulate weather API call
        var temperature = Random.Shared.Next(-10, 35);
        var unit = args.Unit ?? "celsius";
        var unitSymbol = unit == "fahrenheit" ? "°F" : "°C";

        if (unit == "fahrenheit")
        {
            temperature = (int)(temperature * 9.0 / 5.0 + 32);
        }

        var result = JsonSerializer.Serialize(new
        {
            location = args.Location,
            temperature = temperature,
            unit = unit,
            description = "Partly cloudy",
            humidity = Random.Shared.Next(30, 80),
            message = $"The current weather in {args.Location} is {temperature}{unitSymbol} and partly cloudy."
        });

        return Task.FromResult(result);
    }

    private class WeatherArgs
    {
        public string? Location { get; set; }
        public string? Unit { get; set; }
    }
}

/// <summary>
/// Example calculator tool implementation
/// </summary>
public class CalculatorTool : FunctionToolBase
{
    public override FunctionDefinition Definition => new()
    {
        Name = "calculate",
        Description = "Perform basic arithmetic calculations",
        Parameters = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["expression"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "The mathematical expression to evaluate (e.g., '2 + 2', '10 * 5')"
                }
            },
            ["required"] = new[] { "expression" }
        }
    };

    public override Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var args = DeserializeArguments<CalculatorArgs>(argumentsJson);
        if (args?.Expression == null)
        {
            return Task.FromResult("Error: Expression parameter is required");
        }

        try
        {
            // Simple expression evaluator (for demo purposes)
            var result = EvaluateExpression(args.Expression);
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                expression = args.Expression,
                result = result,
                message = $"The result of {args.Expression} is {result}"
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                error = $"Error evaluating expression: {ex.Message}",
                expression = args.Expression
            }));
        }
    }

    private static double EvaluateExpression(string expression)
    {
        // Very simple calculator - only handles basic operations
        // In a real implementation, you'd use a proper expression parser
        var cleanExpression = expression.Replace(" ", "");

        if (cleanExpression.Contains("+"))
        {
            var parts = cleanExpression.Split('+');
            return double.Parse(parts[0]) + double.Parse(parts[1]);
        }
        else if (cleanExpression.Contains("-"))
        {
            var parts = cleanExpression.Split('-');
            return double.Parse(parts[0]) - double.Parse(parts[1]);
        }
        else if (cleanExpression.Contains("*"))
        {
            var parts = cleanExpression.Split('*');
            return double.Parse(parts[0]) * double.Parse(parts[1]);
        }
        else if (cleanExpression.Contains("/"))
        {
            var parts = cleanExpression.Split('/');
            return double.Parse(parts[0]) / double.Parse(parts[1]);
        }
        else
        {
            return double.Parse(cleanExpression);
        }
    }

    private class CalculatorArgs
    {
        public string? Expression { get; set; }
    }
}
