namespace LMSupplyDepots.Inference.Configuration;

/// <summary>
/// Configuration options for a specific inference engine
/// </summary>
public class EngineOptions
{
    /// <summary>
    /// Whether this engine is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Priority of this engine (lower is higher priority)
    /// </summary>
    public int Priority { get; set; } = 100;

    /// <summary>
    /// Engine-specific parameters
    /// </summary>
    public Dictionary<string, object?> Parameters { get; set; } = new();

    /// <summary>
    /// Try to get a parameter value with a specific type
    /// </summary>
    public bool TryGetParameter<T>(string key, out T? value)
    {
        if (Parameters.TryGetValue(key, out var objValue) && objValue is T typedValue)
        {
            value = typedValue;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Get a parameter value with a specific type, or return default if not found
    /// </summary>
    public T? GetParameterOrDefault<T>(string key, T? defaultValue = default)
    {
        if (TryGetParameter<T>(key, out var value))
        {
            return value;
        }

        return defaultValue;
    }
}