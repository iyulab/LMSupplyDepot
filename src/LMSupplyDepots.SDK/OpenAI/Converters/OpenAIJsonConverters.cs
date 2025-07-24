using System.Text.Json;
using System.Text.Json.Serialization;
using LMSupplyDepots.SDK.OpenAI.Models;

namespace LMSupplyDepots.SDK.OpenAI.Converters;

/// <summary>
/// JSON converter for ContentPart that handles both string and object formats
/// </summary>
public class ContentPartConverter : JsonConverter<ContentPart?>
{
    public override ContentPart? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString();
            return string.IsNullOrEmpty(text) ? null : new TextContentPart { Text = text };
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;

            if (root.TryGetProperty("type", out var typeProperty))
            {
                var contentType = typeProperty.GetString();
                return contentType switch
                {
                    "text" => JsonSerializer.Deserialize<TextContentPart>(root.GetRawText(), options),
                    "image_url" => JsonSerializer.Deserialize<ImageContentPart>(root.GetRawText(), options),
                    _ => throw new JsonException($"Unknown content type: {contentType}")
                };
            }

            // If no type property, assume it's a text content part
            if (root.TryGetProperty("text", out var textProperty))
            {
                return new TextContentPart { Text = textProperty.GetString() ?? string.Empty };
            }
        }

        throw new JsonException($"Unable to convert JSON token type {reader.TokenType} to ContentPart");
    }

    public override void Write(Utf8JsonWriter writer, ContentPart? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        // For TextContentPart, write just the text string directly for OpenAI compatibility
        if (value is TextContentPart textPart)
        {
            writer.WriteStringValue(textPart.Text);
            return;
        }

        // For other types, serialize the full object
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}

/// <summary>
/// JSON converter for StopSequence that handles both string and array formats
/// </summary>
public class StopSequenceConverter : JsonConverter<StopSequence?>
{
    public override StopSequence? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString();
            return string.IsNullOrEmpty(value) ? null : new StopSequence { Single = value };
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var list = JsonSerializer.Deserialize<List<string>>(ref reader, options);
            return list == null || list.Count == 0 ? null : new StopSequence { Multiple = list };
        }

        throw new JsonException($"Unable to convert JSON token type {reader.TokenType} to StopSequence");
    }

    public override void Write(Utf8JsonWriter writer, StopSequence? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        if (value.Single != null)
        {
            writer.WriteStringValue(value.Single);
        }
        else if (value.Multiple != null)
        {
            JsonSerializer.Serialize(writer, value.Multiple, options);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}

/// <summary>
/// JSON converter for ToolChoice that handles both string and object formats
/// </summary>
public class ToolChoiceConverter : JsonConverter<ToolChoice?>
{
    public override ToolChoice? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString();
            return string.IsNullOrEmpty(value) ? null : new ToolChoice { Type = value };
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            // Manually deserialize to avoid infinite recursion
            var toolChoice = new ToolChoice();

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    break;

                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    var propertyName = reader.GetString();
                    reader.Read(); // Move to the value

                    switch (propertyName?.ToLowerInvariant())
                    {
                        case "type":
                            toolChoice.Type = reader.GetString();
                            break;
                        case "function":
                            if (reader.TokenType == JsonTokenType.StartObject)
                            {
                                toolChoice.Function = JsonSerializer.Deserialize<FunctionChoice>(ref reader, options);
                            }
                            break;
                        default:
                            reader.Skip(); // Skip unknown properties
                            break;
                    }
                }
            }

            return toolChoice;
        }

        throw new JsonException($"Unable to convert JSON token type {reader.TokenType} to ToolChoice");
    }

    public override void Write(Utf8JsonWriter writer, ToolChoice? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        // If it's a simple string type (like "auto", "none", "required"), write as string
        if (value.Function == null && !string.IsNullOrEmpty(value.Type))
        {
            writer.WriteStringValue(value.Type);
        }
        else
        {
            // Manually write object to avoid infinite recursion
            writer.WriteStartObject();

            if (!string.IsNullOrEmpty(value.Type))
            {
                writer.WritePropertyName("type");
                writer.WriteStringValue(value.Type);
            }

            if (value.Function != null)
            {
                writer.WritePropertyName("function");
                JsonSerializer.Serialize(writer, value.Function, options);
            }

            writer.WriteEndObject();
        }
    }
}
