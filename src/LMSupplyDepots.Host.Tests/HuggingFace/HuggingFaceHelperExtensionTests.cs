using LMSupplyDepots.ModelHub.HuggingFace;
using Xunit;

namespace LMSupplyDepots.Host.Tests.HuggingFace;

/// <summary>
/// Unit tests for HuggingFaceHelper extension handling methods
/// Tests fix for double extension bug (artifact.gguf.gguf)
/// All tests marked as Unit category for fast CICD execution
/// </summary>
public class HuggingFaceHelperExtensionTests
{
    /// <summary>
    /// Tests EnsureGgufExtension with artifact name without extension
    /// </summary>
    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("artifact", "artifact.gguf")]
    [InlineData("model-Q4_K_M", "model-Q4_K_M.gguf")]
    [InlineData("Phi-4-mini-instruct-Q2_K", "Phi-4-mini-instruct-Q2_K.gguf")]
    [Trait("Category", "Unit")]
    public void EnsureGgufExtension_WithoutExtension_AddsExtension(string input, string expected)
    {
        // Act
        var result = HuggingFaceHelper.EnsureGgufExtension(input);

        // Assert
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Tests EnsureGgufExtension with artifact name that already has .gguf extension
    /// Should NOT add duplicate extension
    /// </summary>
    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("artifact.gguf", "artifact.gguf")]
    [InlineData("model-Q4_K_M.gguf", "model-Q4_K_M.gguf")]
    [InlineData("Phi-4-mini-instruct-Q2_K.gguf", "Phi-4-mini-instruct-Q2_K.gguf")]
    public void EnsureGgufExtension_WithExtension_DoesNotDuplicate(string input, string expected)
    {
        // Act
        var result = HuggingFaceHelper.EnsureGgufExtension(input);

        // Assert
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Tests EnsureGgufExtension with case-insensitive .GGUF extension
    /// Should preserve existing extension case
    /// </summary>
    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("artifact.GGUF", "artifact.GGUF")]
    [InlineData("model.Gguf", "model.Gguf")]
    [InlineData("test.GgUf", "test.GgUf")]
    public void EnsureGgufExtension_CaseInsensitive_PreservesCase(string input, string expected)
    {
        // Act
        var result = HuggingFaceHelper.EnsureGgufExtension(input);

        // Assert
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Tests EnsureGgufExtension with edge cases
    /// </summary>
    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(null, null)]
    [InlineData("", "")]
    public void EnsureGgufExtension_EdgeCases_HandlesCorrectly(string? input, string? expected)
    {
        // Act
        var result = HuggingFaceHelper.EnsureGgufExtension(input!);

        // Assert
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Tests RemoveGgufExtension with artifact name that has .gguf extension
    /// </summary>
    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("artifact.gguf", "artifact")]
    [InlineData("model-Q4_K_M.gguf", "model-Q4_K_M")]
    [InlineData("Phi-4-mini-instruct-Q2_K.gguf", "Phi-4-mini-instruct-Q2_K")]
    public void RemoveGgufExtension_WithExtension_RemovesExtension(string input, string expected)
    {
        // Act
        var result = HuggingFaceHelper.RemoveGgufExtension(input);

        // Assert
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Tests RemoveGgufExtension with artifact name without extension
    /// Should return unchanged
    /// </summary>
    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("artifact", "artifact")]
    [InlineData("model-Q4_K_M", "model-Q4_K_M")]
    [InlineData("Phi-4-mini-instruct-Q2_K", "Phi-4-mini-instruct-Q2_K")]
    public void RemoveGgufExtension_WithoutExtension_ReturnsUnchanged(string input, string expected)
    {
        // Act
        var result = HuggingFaceHelper.RemoveGgufExtension(input);

        // Assert
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Tests RemoveGgufExtension with case-insensitive .GGUF extension
    /// </summary>
    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("artifact.GGUF", "artifact")]
    [InlineData("model.Gguf", "model")]
    [InlineData("test.GgUf", "test")]
    public void RemoveGgufExtension_CaseInsensitive_RemovesExtension(string input, string expected)
    {
        // Act
        var result = HuggingFaceHelper.RemoveGgufExtension(input);

        // Assert
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Tests RemoveGgufExtension with edge cases
    /// </summary>
    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(null, null)]
    [InlineData("", "")]
    public void RemoveGgufExtension_EdgeCases_HandlesCorrectly(string? input, string? expected)
    {
        // Act
        var result = HuggingFaceHelper.RemoveGgufExtension(input!);

        // Assert
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Tests round-trip conversion: Remove then Ensure should result in extension
    /// </summary>
    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("artifact.gguf")]
    [InlineData("artifact")]
    [InlineData("model-Q4_K_M.gguf")]
    [InlineData("model-Q4_K_M")]
    public void RoundTrip_RemoveThenEnsure_ResultsInExtension(string input)
    {
        // Act
        var removed = HuggingFaceHelper.RemoveGgufExtension(input);
        var ensured = HuggingFaceHelper.EnsureGgufExtension(removed);

        // Assert
        Assert.EndsWith(".gguf", ensured, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".gguf.gguf", ensured, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tests that double application of EnsureGgufExtension is idempotent
    /// Critical test for the bug fix
    /// </summary>
    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("artifact")]
    [InlineData("artifact.gguf")]
    [InlineData("Phi-4-mini-instruct-Q2_K")]
    [InlineData("Phi-4-mini-instruct-Q2_K.gguf")]
    public void EnsureGgufExtension_DoubleApplication_IsIdempotent(string input)
    {
        // Act
        var firstApplication = HuggingFaceHelper.EnsureGgufExtension(input);
        var secondApplication = HuggingFaceHelper.EnsureGgufExtension(firstApplication);

        // Assert - Should be identical
        Assert.Equal(firstApplication, secondApplication);

        // Assert - Should not have double extension
        Assert.DoesNotContain(".gguf.gguf", secondApplication, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Integration test simulating the bug scenario
    /// User provides "Phi-4-mini-instruct-Q2_K.gguf" as artifact name
    /// Should result in correct filename, not double extension
    /// </summary>
    [Fact]
    [Trait("Category", "Unit")]
    public void BugScenario_ArtifactWithExtension_DoesNotCreateDoubleExtension()
    {
        // Arrange - User input with .gguf extension
        var userInput = "Phi-4-mini-instruct-Q2_K.gguf";

        // Act - Simulate the download flow
        var normalized = HuggingFaceHelper.RemoveGgufExtension(userInput);
        var finalFilename = HuggingFaceHelper.EnsureGgufExtension(normalized);

        // Assert - Should be correct single extension
        Assert.Equal("Phi-4-mini-instruct-Q2_K.gguf", finalFilename);
        Assert.DoesNotContain(".gguf.gguf", finalFilename);
    }

    /// <summary>
    /// Integration test for backward compatibility
    /// User provides artifact name without extension (old behavior)
    /// Should still work correctly
    /// </summary>
    [Fact]
    [Trait("Category", "Unit")]
    public void BackwardCompatibility_ArtifactWithoutExtension_AddsExtension()
    {
        // Arrange - User input without .gguf extension (old behavior)
        var userInput = "microsoft_Phi-4-mini-instruct-Q2_K";

        // Act - Simulate the download flow
        var normalized = HuggingFaceHelper.RemoveGgufExtension(userInput);
        var finalFilename = HuggingFaceHelper.EnsureGgufExtension(normalized);

        // Assert - Should add extension correctly
        Assert.Equal("microsoft_Phi-4-mini-instruct-Q2_K.gguf", finalFilename);
    }
}
