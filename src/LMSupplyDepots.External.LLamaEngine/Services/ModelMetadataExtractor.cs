using LLama;
using LLama.Native;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace LMSupplyDepots.External.LLamaEngine.Services;

/// <summary>
/// Service for extracting metadata and capabilities from GGUF model files
/// </summary>
public class ModelMetadataExtractor
{
    private readonly ILogger<ModelMetadataExtractor> _logger;

    public ModelMetadataExtractor(ILogger<ModelMetadataExtractor> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Extract comprehensive metadata from a loaded model
    /// </summary>
    public ModelMetadata ExtractMetadata(SafeLlamaModelHandle modelHandle)
    {
        try
        {
            _logger.LogDebug("Extracting metadata from model");

            // Read all metadata from the model
            var rawMetadata = modelHandle.ReadMetadata();

            var metadata = new ModelMetadata
            {
                RawMetadata = rawMetadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),

                // Extract architecture information
                Architecture = rawMetadata.TryGetValue("general.architecture", out var arch) ? arch : "unknown",
                ModelName = rawMetadata.TryGetValue("general.name", out var name) ? name : "unknown",
                ModelType = rawMetadata.TryGetValue("general.type", out var type) ? type : "unknown",

                // Extract chat template
                ChatTemplate = ExtractChatTemplate(rawMetadata),

                // Extract special tokens
                SpecialTokens = ExtractSpecialTokens(modelHandle, rawMetadata),

                // Extract tool calling capabilities
                ToolCapabilities = ExtractToolCapabilities(rawMetadata),

                // Extract model parameters
                ContextLength = ExtractContextLength(rawMetadata),
                VocabularySize = ExtractVocabularySize(rawMetadata),
                EmbeddingLength = ExtractEmbeddingLength(rawMetadata),

                // Extract stop tokens from chat template and special tokens
                StopTokens = ExtractStopTokens(rawMetadata)
            };

            _logger.LogInformation("Successfully extracted metadata for {Architecture} model: {ModelName}",
                metadata.Architecture, metadata.ModelName);

            return metadata;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract model metadata");
            throw;
        }
    }

    /// <summary>
    /// Extract chat template from metadata
    /// </summary>
    private string? ExtractChatTemplate(IReadOnlyDictionary<string, string> metadata)
    {
        if (metadata.TryGetValue("tokenizer.chat_template", out var template))
        {
            _logger.LogDebug("Found chat template: {Template}", template.Substring(0, Math.Min(100, template.Length)) + "...");
            return template;
        }

        _logger.LogWarning("No chat template found in model metadata");
        return null;
    }

    /// <summary>
    /// Extract special tokens information
    /// </summary>
    private Dictionary<string, LLamaToken> ExtractSpecialTokens(SafeLlamaModelHandle modelHandle, IReadOnlyDictionary<string, string> metadata)
    {
        var specialTokens = new Dictionary<string, LLamaToken>();

        try
        {
            // Get standard special tokens
            var bosToken = NativeApi.llama_token_bos(modelHandle);
            var eosToken = NativeApi.llama_token_eos(modelHandle);
            var nlToken = NativeApi.llama_token_nl(modelHandle);

            specialTokens["BOS"] = bosToken;
            specialTokens["EOS"] = eosToken;
            specialTokens["NL"] = nlToken;

            // Extract token IDs from metadata
            if (metadata.TryGetValue("tokenizer.ggml.bos_token_id", out var bosIdStr) && int.TryParse(bosIdStr, out var bosId))
                specialTokens["BOS_ID"] = new LLamaToken(bosId);

            if (metadata.TryGetValue("tokenizer.ggml.eos_token_id", out var eosIdStr) && int.TryParse(eosIdStr, out var eosId))
                specialTokens["EOS_ID"] = new LLamaToken(eosId);

            if (metadata.TryGetValue("tokenizer.ggml.unknown_token_id", out var unkIdStr) && int.TryParse(unkIdStr, out var unkId))
                specialTokens["UNK"] = new LLamaToken(unkId);

            if (metadata.TryGetValue("tokenizer.ggml.padding_token_id", out var padIdStr) && int.TryParse(padIdStr, out var padId))
                specialTokens["PAD"] = new LLamaToken(padId);

            // Extract chat template specific tokens from tokenizer vocabulary
            ExtractChatTemplateTokens(specialTokens, metadata, modelHandle);

            _logger.LogDebug("Extracted {Count} special tokens", specialTokens.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract some special tokens");
        }

        return specialTokens;
    }

    /// <summary>
    /// Extract chat template specific tokens from tokenizer vocabulary
    /// </summary>
    private void ExtractChatTemplateTokens(Dictionary<string, LLamaToken> specialTokens, IReadOnlyDictionary<string, string> metadata, SafeLlamaModelHandle modelHandle)
    {
        try
        {
            // Define common chat template tokens to look for
            var chatTokenPatterns = new Dictionary<string, string[]>
            {
                // LLaMA-3/3.1/3.2 tokens
                ["LLAMA3_START_HEADER"] = ["<|start_header_id|>"],
                ["LLAMA3_END_HEADER"] = ["<|end_header_id|>"],
                ["LLAMA3_EOT"] = ["<|eot_id|>"],
                ["LLAMA3_BEGIN_TOOL"] = ["<|begin_of_text|>"],
                
                // Phi tokens
                ["PHI_END"] = ["<|end|>"],
                ["PHI_ASSISTANT"] = ["<|assistant|>"],
                ["PHI_USER"] = ["<|user|>"],
                ["PHI_SYSTEM"] = ["<|system|>"],
                ["PHI_TOOL_START"] = ["<|tool|>"],
                ["PHI_TOOL_END"] = ["<|/tool|>"],
                
                // ChatML tokens (Qwen, many others)
                ["CHATML_START"] = ["<|im_start|>"],
                ["CHATML_END"] = ["<|im_end|>"],
                
                // Mistral/Mixtral tokens
                ["MISTRAL_INST_START"] = ["[INST]"],
                ["MISTRAL_INST_END"] = ["[/INST]"],
                ["MISTRAL_SYS_START"] = ["<<SYS>>"],
                ["MISTRAL_SYS_END"] = ["<</SYS>>"],
                
                // DeepSeek tokens
                ["DEEPSEEK_USER"] = ["User:"],
                ["DEEPSEEK_ASSISTANT"] = ["Assistant:"],
                ["DEEPSEEK_SYSTEM"] = ["System:"],
                
                // Falcon tokens
                ["FALCON_USER"] = ["User:"],
                ["FALCON_ASSISTANT"] = ["Assistant:"],
                ["FALCON_HUMAN"] = ["Human:"],
                ["FALCON_AI"] = ["AI:"],
                
                // Generic instruction tokens
                ["INST_START"] = ["### Instruction:", "### Input:"],
                ["INST_RESPONSE"] = ["### Response:", "### Output:"],
                ["INST_HUMAN"] = ["### Human:", "Human:"],
                ["INST_ASSISTANT"] = ["### Assistant:", "Assistant:"]
            };

            // Try to find these tokens in the model's vocabulary
            foreach (var (tokenKey, tokenVariants) in chatTokenPatterns)
            {
                foreach (var tokenText in tokenVariants)
                {
                    try
                    {
                        // Try to tokenize the text to see if it's a valid token
                        var tokens = modelHandle.Tokenize(tokenText, false, "utf-8");
                        if (tokens.Length == 1) // Single token is ideal
                        {
                            specialTokens[tokenKey] = tokens[0];
                            _logger.LogDebug("Found chat token {Key}: {Text} -> {Token}", tokenKey, tokenText, tokens[0]);
                            break; // Found a match, no need to try other variants
                        }
                    }
                    catch
                    {
                        // Token not found or invalid, continue
                        continue;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract chat template tokens");
        }
    }

    /// <summary>
    /// Extract tool calling capabilities
    /// </summary>
    private ToolCapabilities ExtractToolCapabilities(IReadOnlyDictionary<string, string> metadata)
    {
        var capabilities = new ToolCapabilities();

        // First, try to extract from chat template
        if (metadata.TryGetValue("tokenizer.chat_template", out var chatTemplate))
        {
            var templateAnalysis = AnalyzeChatTemplateForToolCalling(chatTemplate, metadata);
            if (templateAnalysis.SupportsTools)
            {
                capabilities.SupportsToolCalling = true;
                capabilities.ToolCallFormat = templateAnalysis.Format;
                capabilities.ToolTokens = templateAnalysis.ToolTokens;
                capabilities.ToolCallSyntax = templateAnalysis.Syntax;
                
                _logger.LogInformation("Detected tool calling from chat template: {Format} with syntax: {Syntax}", 
                    capabilities.ToolCallFormat, capabilities.ToolCallSyntax);
                return capabilities;
            }
        }

        // Fallback: Check for tool-related tokens in the vocabulary
        var hasToolTokens = metadata.Keys.Any(key =>
            key.Contains("tool") &&
            (key.Contains("token") || key.Contains("id")));

        if (hasToolTokens)
        {
            capabilities.SupportsToolCalling = true;

            // Dynamically determine tool calling format based on architecture and model specifics
            if (metadata.TryGetValue("general.architecture", out var arch))
            {
                var architecture = arch.ToLowerInvariant();
                var formatInfo = DetectToolFormatFromArchitecture(architecture, metadata);
                
                capabilities.ToolCallFormat = formatInfo.Format;
                capabilities.ToolCallSyntax = formatInfo.Syntax;
            }
            else
            {
                capabilities.ToolCallFormat = "generic";
                capabilities.ToolCallSyntax = "json";
            }

            // Extract tool-specific tokens
            var toolTokens = metadata
                .Where(kvp => kvp.Key.Contains("tool") && kvp.Key.Contains("token"))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            capabilities.ToolTokens = toolTokens;

            _logger.LogInformation("Model supports tool calling with format: {Format}", capabilities.ToolCallFormat);
        }
        else
        {
            _logger.LogInformation("Model does not appear to support tool calling");
        }

        return capabilities;
    }

    /// <summary>
    /// Analyze chat template for tool calling patterns
    /// </summary>
    private ToolTemplateAnalysis AnalyzeChatTemplateForToolCalling(string chatTemplate, IReadOnlyDictionary<string, string> metadata)
    {
        var analysis = new ToolTemplateAnalysis();

        try
        {
            // Check for known tool calling patterns in chat templates
            var toolPatterns = new Dictionary<string, (string Format, string Syntax, Dictionary<string, string> Tokens)>
            {
                // LLaMA-3.2+ with native tool calling
                [@"(?i)tools.*\|\|.*function"] = ("llama-native", "json", new Dictionary<string, string>
                {
                    ["tool_call_start"] = "<|start_header_id|>assistant<|end_header_id|>",
                    ["tool_call_end"] = "<|eot_id|>",
                    ["function_start"] = "<|python_tag|>",
                    ["function_end"] = "<|eot_id|>"
                }),

                // Phi models with <|tool|> syntax
                [@"<\|tool\|>"] = ("phi", "xml", new Dictionary<string, string>
                {
                    ["tool_start"] = "<|tool|>",
                    ["tool_end"] = "<|/tool|>",
                    ["call_start"] = "<|tool_call|>",
                    ["call_end"] = "<|/tool_call|>"
                }),

                // ChatML format (Qwen, many others)
                [@"<\|im_start\|>tool"] = ("chatml", "json", new Dictionary<string, string>
                {
                    ["tool_start"] = "<|im_start|>tool",
                    ["tool_end"] = "<|im_end|>",
                    ["function_start"] = "<|im_start|>function",
                    ["function_end"] = "<|im_end|>"
                }),

                // Mistral tool format
                [@"\[TOOL_CALLS\]"] = ("mistral", "json", new Dictionary<string, string>
                {
                    ["tool_start"] = "[TOOL_CALLS]",
                    ["tool_end"] = "[/TOOL_CALLS]",
                    ["function_start"] = "{",
                    ["function_end"] = "}"
                }),

                // DeepSeek tool format
                [@"```.*tool_call"] = ("deepseek", "markdown", new Dictionary<string, string>
                {
                    ["tool_start"] = "```tool_call",
                    ["tool_end"] = "```",
                    ["function_start"] = "{",
                    ["function_end"] = "}"
                }),

                // Gemma/CodeGemma tool format
                [@"<start_of_turn>tool"] = ("gemma", "json", new Dictionary<string, string>
                {
                    ["tool_start"] = "<start_of_turn>tool",
                    ["tool_end"] = "<end_of_turn>",
                    ["function_start"] = "{",
                    ["function_end"] = "}"
                }),

                // Generic function calling patterns
                [@"function.*call"] = ("function", "json", new Dictionary<string, string>
                {
                    ["function_start"] = "function_call:",
                    ["function_end"] = "\n",
                    ["tool_start"] = "tool:",
                    ["tool_end"] = "\n"
                })
            };

            foreach (var (pattern, (format, syntax, tokens)) in toolPatterns)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(chatTemplate, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    analysis.SupportsTools = true;
                    analysis.Format = format;
                    analysis.Syntax = syntax;
                    analysis.ToolTokens = tokens;
                    
                    _logger.LogDebug("Found tool pattern {Pattern} -> Format: {Format}, Syntax: {Syntax}", 
                        pattern, format, syntax);
                    break;
                }
            }

            // Additional checks for tool-related keywords
            if (!analysis.SupportsTools)
            {
                var toolKeywords = new[] { "tool", "function", "call", "invoke", "execute" };
                var foundKeywords = toolKeywords.Where(keyword => 
                    chatTemplate.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();

                if (foundKeywords.Count >= 2)
                {
                    analysis.SupportsTools = true;
                    analysis.Format = DetectFormatFromArchitecture(metadata);
                    analysis.Syntax = "json"; // Default to JSON
                    analysis.ToolTokens = new Dictionary<string, string>();
                    
                    _logger.LogDebug("Detected potential tool support from keywords: {Keywords}", 
                        string.Join(", ", foundKeywords));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to analyze chat template for tool calling");
        }

        return analysis;
    }

    /// <summary>
    /// Detect tool format from architecture when template analysis fails
    /// </summary>
    private (string Format, string Syntax) DetectToolFormatFromArchitecture(string architecture, IReadOnlyDictionary<string, string> metadata)
    {
        return architecture switch
        {
            "phi3" or "phi" => DetectPhiToolVariant(metadata),
            "llama" => DetectLlamaToolVariant(metadata),
            "mixtral" or "mistral" => ("mistral", "json"),
            "qwen" or "qwen2" => ("chatml", "json"),
            "deepseek" => ("deepseek", "markdown"),
            "gemma" => ("gemma", "json"),
            _ => ("generic", "json")
        };
    }

    /// <summary>
    /// Detect format from architecture metadata
    /// </summary>
    private string DetectFormatFromArchitecture(IReadOnlyDictionary<string, string> metadata)
    {
        if (metadata.TryGetValue("general.architecture", out var arch))
        {
            return arch.ToLowerInvariant() switch
            {
                "phi3" or "phi" => "phi",
                "llama" => "llama-native",
                "mixtral" or "mistral" => "mistral",
                "qwen" or "qwen2" => "chatml",
                "deepseek" => "deepseek",
                "gemma" => "gemma",
                _ => "generic"
            };
        }
        return "generic";
    }

    /// <summary>
    /// Detect specific Phi tool calling variant
    /// </summary>
    private (string Format, string Syntax) DetectPhiToolVariant(IReadOnlyDictionary<string, string> metadata)
    {
        if (metadata.TryGetValue("general.name", out var modelName))
        {
            var name = modelName.ToLowerInvariant();
            if (name.Contains("phi-4") || name.Contains("phi4"))
            {
                return ("phi4", "xml");
            }
            if (name.Contains("phi-3.5") || name.Contains("phi3.5"))
            {
                return ("phi3.5", "xml");
            }
        }
        return ("phi3", "xml");
    }

    /// <summary>
    /// Detect specific LLaMA tool calling variant
    /// </summary>
    private (string Format, string Syntax) DetectLlamaToolVariant(IReadOnlyDictionary<string, string> metadata)
    {
        if (metadata.TryGetValue("general.name", out var modelName))
        {
            var name = modelName.ToLowerInvariant();
            if (name.Contains("llama-3.2") || name.Contains("llama-3.1"))
            {
                return ("llama-native", "json");
            }
            if (name.Contains("llama-3"))
            {
                return ("llama3", "json");
            }
        }
        return ("llama2", "bracket");
    }

    /// <summary>
    /// Detect specific Phi model variant based on metadata
    /// </summary>
    private string DetectPhiVariant(IReadOnlyDictionary<string, string> metadata)
    {
        // Check model name for specific variants
        if (metadata.TryGetValue("general.name", out var modelName))
        {
            var name = modelName.ToLowerInvariant();
            if (name.Contains("phi-4") || name.Contains("phi4"))
            {
                return "phi4";
            }
            if (name.Contains("phi-3.5") || name.Contains("phi3.5"))
            {
                return "phi3.5";
            }
            if (name.Contains("phi-3") || name.Contains("phi3"))
            {
                return "phi3";
            }
        }

        // Default to phi3 format for unknown Phi variants
        return "phi3";
    }

    /// <summary>
    /// Extract context length
    /// </summary>
    private int ExtractContextLength(IReadOnlyDictionary<string, string> metadata)
    {
        var keys = new[] { "llama.context_length", "phi3.context_length", "general.context_length" };

        foreach (var key in keys)
        {
            if (metadata.TryGetValue(key, out var value) && int.TryParse(value, out var contextLength))
            {
                return contextLength;
            }
        }

        _logger.LogWarning("Could not determine context length from metadata");
        return 2048; // Default fallback
    }

    /// <summary>
    /// Extract vocabulary size
    /// </summary>
    private int ExtractVocabularySize(IReadOnlyDictionary<string, string> metadata)
    {
        if (metadata.TryGetValue("tokenizer.ggml.tokens", out var tokensValue))
        {
            // The tokens value is usually in format "arr[str,COUNT]"
            var match = System.Text.RegularExpressions.Regex.Match(tokensValue, @"arr\[str,(\d+)\]");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var vocabSize))
            {
                return vocabSize;
            }
        }

        _logger.LogWarning("Could not determine vocabulary size from metadata");
        return 32000; // Default fallback
    }

    /// <summary>
    /// Extract embedding length
    /// </summary>
    private int ExtractEmbeddingLength(IReadOnlyDictionary<string, string> metadata)
    {
        var keys = new[] { "llama.embedding_length", "phi3.embedding_length", "general.embedding_length" };

        foreach (var key in keys)
        {
            if (metadata.TryGetValue(key, out var value) && int.TryParse(value, out var embeddingLength))
            {
                return embeddingLength;
            }
        }

        return 0; // No embeddings supported
    }

    /// <summary>
    /// Extract stop tokens from chat template and model architecture
    /// </summary>
    private List<string> ExtractStopTokens(IReadOnlyDictionary<string, string> metadata)
    {
        var stopTokens = new List<string>();

        try
        {
            // First, try to extract from chat template
            if (metadata.TryGetValue("tokenizer.chat_template", out var chatTemplate))
            {
                stopTokens.AddRange(ExtractStopTokensFromChatTemplate(chatTemplate));
            }

            // Add architecture-specific defaults
            if (metadata.TryGetValue("general.architecture", out var architecture))
            {
                stopTokens.AddRange(GetArchitectureSpecificStopTokens(architecture, metadata));
            }

            // Remove duplicates and empty strings
            stopTokens = stopTokens.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();

            _logger.LogInformation("Extracted {Count} stop tokens: {Tokens}", stopTokens.Count, string.Join(", ", stopTokens.Take(5)));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract stop tokens");
        }

        return stopTokens;
    }

    /// <summary>
    /// Extract stop tokens from chat template content
    /// </summary>
    private List<string> ExtractStopTokensFromChatTemplate(string chatTemplate)
    {
        var stopTokens = new List<string>();

        try
        {
            // Common patterns to look for in chat templates
            var patterns = new[]
            {
                // LLaMA-3 patterns
                @"<\|eot_id\|>",
                @"<\|start_header_id\|>",
                @"<\|end_header_id\|>",
                
                // Phi patterns
                @"<\|end\|>",
                @"<\|assistant\|>",
                @"<\|user\|>",
                @"<\|system\|>",
                
                // ChatML patterns
                @"<\|im_start\|>",
                @"<\|im_end\|>",
                
                // Mistral patterns
                @"\[INST\]",
                @"\[/INST\]",
                @"<<SYS>>",
                @"<</SYS>>",
                
                // Role patterns
                @"\buser:",
                @"\bassistant:",
                @"\bsystem:",
                @"\bUser:",
                @"\bAssistant:",
                @"\bSystem:",
                @"\bHuman:",
                @"\bAI:",
                
                // Instruction patterns
                @"### Instruction:",
                @"### Response:",
                @"### Human:",
                @"### Assistant:"
            };

            foreach (var pattern in patterns)
            {
                var matches = System.Text.RegularExpressions.Regex.Matches(chatTemplate, pattern);
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    var token = match.Value;
                    if (!string.IsNullOrWhiteSpace(token) && !stopTokens.Contains(token))
                    {
                        stopTokens.Add(token);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract stop tokens from chat template");
        }

        return stopTokens;
    }

    /// <summary>
    /// Get architecture-specific stop tokens
    /// </summary>
    private List<string> GetArchitectureSpecificStopTokens(string architecture, IReadOnlyDictionary<string, string> metadata)
    {
        var arch = architecture.ToLowerInvariant();
        
        return arch switch
        {
            "llama" => GetLlamaStopTokens(metadata),
            "phi3" or "phi" => GetPhiStopTokens(metadata),
            "mistral" or "mixtral" => GetMistralStopTokens(),
            "qwen" or "qwen2" => GetQwenStopTokens(),
            "deepseek" => GetDeepSeekStopTokens(),
            "falcon" => GetFalconStopTokens(),
            "gemma" => GetGemmaStopTokens(),
            _ => GetGenericStopTokens()
        };
    }

    private List<string> GetLlamaStopTokens(IReadOnlyDictionary<string, string> metadata)
    {
        var tokens = new List<string> { "</s>" }; // Basic EOS

        // Check if it's LLaMA-3 family
        if (metadata.TryGetValue("general.name", out var modelName) && 
            (modelName.Contains("Llama-3") || modelName.Contains("llama-3")))
        {
            tokens.AddRange(new[]
            {
                "<|eot_id|>",
                "<|start_header_id|>user<|end_header_id|>",
                "<|start_header_id|>assistant<|end_header_id|>",
                "<|start_header_id|>system<|end_header_id|>",
                "\nuser:",
                "\nassistant:",
                "\nsystem:"
            });
        }
        else
        {
            // LLaMA-2 or earlier
            tokens.AddRange(new[]
            {
                "[INST]",
                "[/INST]",
                "<<SYS>>",
                "<</SYS>>",
                "\nUser:",
                "\nAssistant:"
            });
        }

        return tokens;
    }

    private List<string> GetPhiStopTokens(IReadOnlyDictionary<string, string> metadata)
    {
        return new List<string>
        {
            "<|end|>",
            "<|assistant|>",
            "<|user|>",
            "<|system|>",
            "\nuser:",
            "\nassistant:",
            "\nsystem:"
        };
    }

    private List<string> GetMistralStopTokens()
    {
        return new List<string>
        {
            "</s>",
            "[INST]",
            "[/INST]",
            "\nUser:",
            "\nAssistant:"
        };
    }

    private List<string> GetQwenStopTokens()
    {
        return new List<string>
        {
            "<|im_end|>",
            "<|im_start|>user",
            "<|im_start|>assistant",
            "<|im_start|>system",
            "\nuser:",
            "\nassistant:",
            "\nsystem:"
        };
    }

    private List<string> GetDeepSeekStopTokens()
    {
        return new List<string>
        {
            "</s>",
            "User:",
            "Assistant:",
            "System:",
            "\nUser:",
            "\nAssistant:",
            "\nSystem:"
        };
    }

    private List<string> GetFalconStopTokens()
    {
        return new List<string>
        {
            "</s>",
            "User:",
            "Assistant:",
            "Human:",
            "AI:",
            "\nUser:",
            "\nAssistant:",
            "\nHuman:",
            "\nAI:"
        };
    }

    private List<string> GetGemmaStopTokens()
    {
        return new List<string>
        {
            "<eos>",
            "<end_of_turn>",
            "\nuser:",
            "\nassistant:",
            "\nmodel:"
        };
    }

    private List<string> GetGenericStopTokens()
    {
        return new List<string>
        {
            "</s>",
            "<|endoftext|>",
            "\nUser:",
            "\nAssistant:",
            "\nHuman:",
            "\nAI:"
        };
    }
}

/// <summary>
/// Comprehensive model metadata extracted from GGUF file
/// </summary>
public class ModelMetadata
{
    public string Architecture { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string ModelType { get; set; } = string.Empty;
    public string? ChatTemplate { get; set; }
    public Dictionary<string, LLamaToken> SpecialTokens { get; set; } = new();
    public ToolCapabilities ToolCapabilities { get; set; } = new();
    public int ContextLength { get; set; }
    public int VocabularySize { get; set; }
    public int EmbeddingLength { get; set; }
    public List<string> StopTokens { get; set; } = new();
    public Dictionary<string, string> RawMetadata { get; set; } = new();
}

/// <summary>
/// Tool calling capabilities information
/// </summary>
public class ToolCapabilities
{
    public bool SupportsToolCalling { get; set; }
    public string ToolCallFormat { get; set; } = string.Empty;
    public string ToolCallSyntax { get; set; } = string.Empty;
    public Dictionary<string, string> ToolTokens { get; set; } = new();
}

/// <summary>
/// Tool template analysis result
/// </summary>
public class ToolTemplateAnalysis
{
    public bool SupportsTools { get; set; }
    public string Format { get; set; } = string.Empty;
    public string Syntax { get; set; } = string.Empty;
    public Dictionary<string, string> ToolTokens { get; set; } = new();
}
