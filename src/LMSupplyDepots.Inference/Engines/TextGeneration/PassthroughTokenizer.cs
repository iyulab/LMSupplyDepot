namespace LMSupplyDepots.Inference.Engines.TextGeneration;

/// <summary>
/// Simple passthrough tokenizer that estimates token count based on character count
/// </summary>
public class PassthroughTokenizer
{
    private readonly double _charactersPerToken;

    /// <summary>
    /// Initializes a new instance of the PassthroughTokenizer
    /// </summary>
    public PassthroughTokenizer(double charactersPerToken = 4.0)
    {
        _charactersPerToken = charactersPerToken;
    }

    /// <summary>
    /// Estimate token count for a text
    /// </summary>
    public int CountTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        return (int)Math.Ceiling(text.Length / _charactersPerToken);
    }

    /// <summary>
    /// Tokenize text into tokens (just returns the original text)
    /// </summary>
    public string[] Tokenize(string text)
    {
        // This is a very simplistic implementation that doesn't actually tokenize
        return new[] { text };
    }
}