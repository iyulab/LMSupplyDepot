using System.Text.Json;
using System.Text.Json.Serialization;

namespace LMSupplyDepots.Utils;

public static class JsonHelper
{
    private static readonly JsonSerializerOptions DefaultOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Serialize an object to JSON string with enum values as strings
    /// </summary>
    public static string Serialize<T>(T obj)
    {
        return JsonSerializer.Serialize(obj, DefaultOptions);
    }

    /// <summary>
    /// Serialize an object to JSON string with custom options
    /// </summary>
    public static string Serialize<T>(T obj, JsonSerializerOptions options)
    {
        return JsonSerializer.Serialize(obj, options);
    }

    /// <summary>
    /// Deserialize JSON string to an object with enum values as strings
    /// </summary>
    public static T? Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, DefaultOptions);
    }

    /// <summary>
    /// Deserialize JSON string to an object with custom options
    /// </summary>
    public static T? Deserialize<T>(string json, JsonSerializerOptions options)
    {
        return JsonSerializer.Deserialize<T>(json, options);
    }

    /// <summary>
    /// Try to deserialize JSON string to an object, returns default value if fails
    /// </summary>
    public static T? TryDeserialize<T>(string json)
    {
        try
        {
            return Deserialize<T>(json);
        }
        catch (JsonException)
        {
            return default;
        }
    }
}