using LMSupplyDepots.Inference.Engines.TextGeneration;

namespace LMSupplyDepots.Inference.Services;

/// <summary>
/// Service for tokenization operations
/// </summary>
public class TokenizerService
{
    private readonly ILogger<TokenizerService> _logger;
    private readonly ConcurrentDictionary<string, PassthroughTokenizer> _tokenizers = new();

    /// <summary>
    /// Initializes a new instance of the TokenizerService
    /// </summary>
    public TokenizerService(ILogger<TokenizerService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets a tokenizer for a model
    /// </summary>
    public PassthroughTokenizer GetTokenizer(string modelId, double? charactersPerToken = null)
    {
        return _tokenizers.GetOrAdd(modelId, _ =>
            new PassthroughTokenizer(charactersPerToken ?? 4.0));
    }

    /// <summary>
    /// Counts tokens in a text for a specific model
    /// </summary>
    public int CountTokens(string modelId, string text)
    {
        var tokenizer = GetTokenizer(modelId);
        return tokenizer.CountTokens(text);
    }

    /// <summary>
    /// Tokenizes text for a specific model
    /// </summary>
    public string[] Tokenize(string modelId, string text)
    {
        var tokenizer = GetTokenizer(modelId);
        return tokenizer.Tokenize(text);
    }

    /// <summary>
    /// Registers a model-specific tokenizer configuration
    /// </summary>
    public void RegisterModelTokenizer(string modelId, double charactersPerToken)
    {
        _tokenizers[modelId] = new PassthroughTokenizer(charactersPerToken);
        _logger.LogDebug("Registered tokenizer for model {ModelId} with {CharsPerToken} characters per token",
            modelId, charactersPerToken);
    }
}