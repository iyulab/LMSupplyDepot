using LMSupplyDepots.External.HuggingFace.Common;
using LMSupplyDepots.External.HuggingFace.Models;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LMSupplyDepots.External.HuggingFace.Tests;

public class HuggingFaceModelTests
{
    [Fact]
    public void GetFilePaths_NoPattern_ReturnsAllFiles()
    {
        // Arrange
        var jsonString = @"{
            ""_id"": ""test"",
            ""siblings"": [
                { ""rfilename"": ""config.json"" },
                { ""rfilename"": ""model.bin"" },
                { ""rfilename"": ""vocab.txt"" }
            ]
        }";

        var model = JsonSerializer.Deserialize<HuggingFaceModel>(jsonString);

        // Act
        var result = model!.GetFilePaths();

        // Assert
        Assert.Equal(3, result.Length);
        Assert.Contains("config.json", result);
        Assert.Contains("model.bin", result);
        Assert.Contains("vocab.txt", result);
    }

    [Fact]
    public void GetFilePaths_WithPattern_ReturnsMatchingFiles()
    {
        // Arrange
        var jsonString = @"{
            ""_id"": ""test"",
            ""siblings"": [
                { ""rfilename"": ""config.json"" },
                { ""rfilename"": ""model.bin"" },
                { ""rfilename"": ""vocab.txt"" }
            ]
        }";

        var model = JsonSerializer.Deserialize<HuggingFaceModel>(jsonString);
        var pattern = new Regex(@"\.json$");

        // Act
        var result = model!.GetFilePaths(pattern);

        // Assert
        Assert.Single(result);
        Assert.Equal("config.json", result[0]);
    }

    [Fact]
    public void IsTextGenerationModel_WithValidTags_ReturnsExpectedResult()
    {
        // Arrange
        var jsonString = @"{
            ""_id"": ""test"",
            ""tags"": [""text-generation"", ""gguf""]
        }";

        var model = JsonSerializer.Deserialize<HuggingFaceModel>(jsonString);

        // Act
        var result = ModelTagValidation.IsTextGenerationModel(model!);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsEmbeddingModel_WithValidTags_ReturnsExpectedResult()
    {
        // Arrange
        var jsonString = @"{
            ""_id"": ""test"",
            ""tags"": [""sentence-similarity"", ""gguf""]
        }";

        var model = JsonSerializer.Deserialize<HuggingFaceModel>(jsonString);

        // Act
        var result = ModelTagValidation.IsEmbeddingModel(model!);

        // Assert
        Assert.True(result);
    }
}