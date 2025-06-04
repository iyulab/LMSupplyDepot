namespace LMSupplyDepots.Utils;

/// <summary>
/// Helper methods for token-related operations.
/// </summary>
public static class TokenizationHelpers
{
    /// <summary>
    /// Simple estimation of token count based on character count.
    /// </summary>
    /// <remarks>
    /// This is a rough heuristic and not a replacement for real tokenization,
    /// but useful for simple estimation when a tokenizer isn't available.
    /// </remarks>
    public static int EstimateTokenCount(string text, double charactersPerToken = 4.0)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        return (int)Math.Ceiling(text.Length / charactersPerToken);
    }
}
