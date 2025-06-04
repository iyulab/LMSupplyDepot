namespace LMSupplyDepots.Utils;

/// <summary>
/// Extension methods for string operations.
/// </summary>
public static class StringExtensions
{
    /// <summary>
    /// Truncates a string to a maximum length and adds an ellipsis if truncated.
    /// </summary>
    public static string Truncate(this string value, int maxLength, string suffix = "...")
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;

        return string.Concat(value.AsSpan(0, maxLength - suffix.Length), suffix);
    }

    public static string ToDashCase(this string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        return string.Concat(value.Select(c => char.IsUpper(c) ? "-" + c.ToString().ToLower() : c.ToString())).TrimStart('-');
    }   
}