using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LMSupplyDepots.External.HuggingFace.Models;

public class Sibling
{
    [JsonPropertyName("rfilename")]
    public string Filename { get; set; } = string.Empty;
}

[JsonConverter(typeof(HuggingFaceModelConverter))]
public class HuggingFaceModel
{
    [JsonPropertyName("_id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("modelId")]
    public string ModelId { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("downloads")]
    public int Downloads { get; set; }

    [JsonPropertyName("likes")]
    public int Likes { get; set; }

    [JsonPropertyName("lastModified")]
    public DateTimeOffset LastModified { get; set; }

    [JsonPropertyName("private")]
    public bool IsPrivate { get; set; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("pipeline_tag")]
    public string PipelineTag { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("library_name")]
    public string LibraryName { get; set; } = string.Empty;

    [JsonPropertyName("siblings")]
    public List<Sibling> Siblings { get; set; } = [];

    [JsonPropertyName("gguf")]
    public Dictionary<string, JsonElement> GGUF { get; set; } = [];

    private readonly JsonDocument? _rawJson;

    [JsonIgnore]
    private readonly Dictionary<string, JsonElement> _additionalProperties = [];

    public HuggingFaceModel()
    {
    }

    [JsonConstructor]
    public HuggingFaceModel(JsonDocument? rawJson = null)
    {
        _rawJson = rawJson;
        if (_rawJson != null)
        {
            foreach (var property in _rawJson.RootElement.EnumerateObject())
            {
                var propertyName = property.Name;
                if (propertyName != "_id" &&
                    propertyName != "modelId" &&
                    propertyName != "author" &&
                    propertyName != "downloads" &&
                    propertyName != "likes" &&
                    propertyName != "lastModified" &&
                    propertyName != "private" &&
                    propertyName != "tags" &&
                    propertyName != "siblings" &&
                    propertyName != "gguf")
                {
                    _additionalProperties[propertyName] = property.Value.Clone();
                }
            }
        }
    }

    public T? GetProperty<T>(string propertyName)
    {
        if (_additionalProperties.TryGetValue(propertyName, out var element))
        {
            try
            {
                return element.Deserialize<T>();
            }
            catch (JsonException)
            {
                return default;
            }
        }
        return default;
    }

    public JsonElement? GetRawProperty(string propertyName)
    {
        return _additionalProperties.TryGetValue(propertyName, out var element) ? element : null;
    }

    public string[] GetFilePaths(Regex? pattern = null)
    {
        var siblings = this.Siblings;
        if (siblings == null || siblings.Count == 0)
            return [];

        var paths = siblings
            .Select(s => s.Filename)
            .Where(f => !string.IsNullOrEmpty(f));

        if (pattern != null)
            return paths.Where(p => pattern.IsMatch(p)).ToArray();

        return [.. paths];
    }

    public bool HasProperty(string propertyName)
    {
        return _additionalProperties.ContainsKey(propertyName);
    }

    public IEnumerable<string> GetAvailableProperties()
    {
        return _additionalProperties.Keys;
    }

    public class HuggingFaceModelConverter : JsonConverter<HuggingFaceModel>
    {
        public override HuggingFaceModel Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var model = new HuggingFaceModel(document);

            if (document.RootElement.TryGetProperty("_id", out var idElement))
            {
                model.Id = idElement.GetString() ?? string.Empty;
            }

            if (document.RootElement.TryGetProperty("modelId", out var modelIdElement))
            {
                model.ModelId = modelIdElement.GetString() ?? string.Empty;
            }

            if (document.RootElement.TryGetProperty("author", out var authorElement))
            {
                model.Author = authorElement.GetString() ?? string.Empty;
            }

            if (document.RootElement.TryGetProperty("downloads", out var downloadsElement))
            {
                model.Downloads = downloadsElement.GetInt32();
            }

            if (document.RootElement.TryGetProperty("likes", out var likesElement))
            {
                model.Likes = likesElement.GetInt32();
            }

            if (document.RootElement.TryGetProperty("lastModified", out var lastModifiedElement))
            {
                model.LastModified = lastModifiedElement.GetDateTimeOffset();
            }

            if (document.RootElement.TryGetProperty("private", out var privateElement))
            {
                model.IsPrivate = privateElement.GetBoolean();
            }

            if (document.RootElement.TryGetProperty("tags", out var tagsElement))
            {
                model.Tags = tagsElement.Deserialize<List<string>>(options) ?? [];
            }

            if (document.RootElement.TryGetProperty("pipeline_tag", out var pipelineTagElement))
            {
                model.PipelineTag = pipelineTagElement.GetString() ?? string.Empty;
            }

            if (document.RootElement.TryGetProperty("createdAt", out var createdAtElement))
            {
                model.CreatedAt = createdAtElement.GetDateTimeOffset();
            }

            if (document.RootElement.TryGetProperty("library_name", out var libraryNameElement))
            {
                model.LibraryName = libraryNameElement.GetString() ?? string.Empty;
            }

            if (document.RootElement.TryGetProperty("siblings", out var siblingsElement))
            {
                model.Siblings = siblingsElement.Deserialize<List<Sibling>>(options) ?? [];
            }

            if (document.RootElement.TryGetProperty("gguf", out var ggufElement))
            {
                model.GGUF = ggufElement.Deserialize<Dictionary<string, JsonElement>>(options) ?? [];
            }

            return model;
        }

        public override void Write(Utf8JsonWriter writer, HuggingFaceModel value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            writer.WriteString("_id", value.Id);
            writer.WriteString("modelId", value.ModelId);
            writer.WriteString("author", value.Author);
            writer.WriteNumber("downloads", value.Downloads);
            writer.WriteNumber("likes", value.Likes);
            writer.WriteString("lastModified", value.LastModified);
            writer.WriteBoolean("private", value.IsPrivate);

            writer.WritePropertyName("tags");
            JsonSerializer.Serialize(writer, value.Tags, options);

            writer.WriteString("pipeline_tag", value.PipelineTag);
            writer.WriteString("createdAt", value.CreatedAt);
            writer.WriteString("library_name", value.LibraryName);

            writer.WritePropertyName("siblings");
            JsonSerializer.Serialize(writer, value.Siblings, options);

            writer.WritePropertyName("gguf");
            JsonSerializer.Serialize(writer, value.GGUF, options);

            foreach (var prop in value._additionalProperties)
            {
                writer.WritePropertyName(prop.Key);
                prop.Value.WriteTo(writer);
            }

            writer.WriteEndObject();
        }
    }
}

public static class HuggingFaceModelExtensions
{
    public static bool GetBooleanProperty(this HuggingFaceModel model, string propertyName, bool defaultValue = false)
    {
        var value = model.GetProperty<bool?>(propertyName);
        return value.GetValueOrDefault(defaultValue);
    }

    public static int GetIntegerProperty(this HuggingFaceModel model, string propertyName, int defaultValue = 0)
    {
        var value = model.GetProperty<int?>(propertyName);
        return value.GetValueOrDefault(defaultValue);
    }

    public static string GetStringProperty(this HuggingFaceModel model, string propertyName, string defaultValue = "")
    {
        return model.GetProperty<string>(propertyName) ?? defaultValue;
    }

    public static List<T> GetListProperty<T>(this HuggingFaceModel model, string propertyName)
    {
        return model.GetProperty<List<T>>(propertyName) ?? [];
    }
}