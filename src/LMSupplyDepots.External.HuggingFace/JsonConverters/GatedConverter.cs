using System.Text.Json.Serialization;
using System.Text.Json;

namespace LMSupplyDepots.External.HuggingFace.JsonConverters;

public class GatedConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString() ?? string.Empty,
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            JsonTokenType.Null => string.Empty,
            _ => throw new JsonException($"Unexpected token type when converting gated value: {reader.TokenType}")
        };
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        if (string.IsNullOrEmpty(value))
        {
            writer.WriteNullValue();
            return;
        }

        if (bool.TryParse(value, out bool boolValue))
        {
            writer.WriteBooleanValue(boolValue);
        }
        else
        {
            writer.WriteStringValue(value);
        }
    }
}