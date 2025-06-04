using System.ComponentModel;

namespace LMSupplyDepots.External.HuggingFace.Models;

/// <summary>
/// Defines sorting options for model search results.
/// </summary>
public enum ModelSortField
{
    /// <summary>
    /// Sort by download count
    /// </summary>
    [Description("downloads")]
    Downloads,

    /// <summary>
    /// Sort by like count
    /// </summary>
    [Description("likes")]
    Likes,

    /// <summary>
    /// Sort by model creation date
    /// </summary>
    [Description("createdAt")]
    CreatedAt,

    /// <summary>
    /// Sort by last modified date
    /// </summary>
    [Description("lastModified")]
    LastModified
}

/// <summary>
/// Extension methods for ModelSortField enum.
/// </summary>
public static class ModelSortFieldExtensions
{
    /// <summary>
    /// Converts the enum value to its corresponding API string value.
    /// </summary>
    public static string ToApiString(this ModelSortField sortField)
    {
        var fieldInfo = sortField.GetType().GetField(sortField.ToString());
        var attribute = (DescriptionAttribute?)Attribute.GetCustomAttribute(
            fieldInfo!,
            typeof(DescriptionAttribute));

        return attribute?.Description ?? sortField.ToString().ToLowerInvariant();
    }
}