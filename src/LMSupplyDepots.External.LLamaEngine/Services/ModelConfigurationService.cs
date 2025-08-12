using LMSupplyDepots.External.LLamaEngine.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace LMSupplyDepots.External.LLamaEngine.Services;

/// <summary>
/// Service interface for model configuration management
/// </summary>
public interface IModelConfigurationService
{
    /// <summary>
    /// Detects the model family and type from a model file
    /// </summary>
    /// <param name="modelPath">Path to the model file</param>
    /// <returns>Detected model information</returns>
    Task<ModelDetectionResult> DetectModelInfoAsync(string modelPath);

    /// <summary>
    /// Creates an optimal configuration for a model
    /// </summary>
    /// <param name="modelPath">Path to the model file</param>
    /// <param name="customSettings">Custom settings to override defaults</param>
    /// <returns>Optimized model configuration</returns>
    Task<ModelConfig> CreateOptimalConfigAsync(string modelPath, Dictionary<string, object>? customSettings = null);

    /// <summary>
    /// Validates a model configuration
    /// </summary>
    /// <param name="config">Configuration to validate</param>
    /// <returns>Validation result</returns>
    ModelConfigValidationResult ValidateConfiguration(ModelConfig config);

    /// <summary>
    /// Suggests performance optimizations for a configuration
    /// </summary>
    /// <param name="config">Model configuration</param>
    /// <param name="systemInfo">System information for optimization</param>
    /// <returns>Performance suggestions</returns>
    List<PerformanceSuggestion> SuggestOptimizations(ModelConfig config, SystemInfo? systemInfo = null);

    /// <summary>
    /// Saves a configuration to file
    /// </summary>
    /// <param name="config">Configuration to save</param>
    /// <param name="filePath">Path to save the configuration</param>
    /// <returns>True if successful</returns>
    Task<bool> SaveConfigurationAsync(ModelConfig config, string filePath);

    /// <summary>
    /// Auto-detects and sets the appropriate chat template
    /// </summary>
    /// <param name="config">Model configuration</param>
    /// <returns>Updated configuration with template</returns>
    ModelConfig AutoDetectChatTemplate(ModelConfig config);
}

/// <summary>
/// Service for model configuration management and optimization
/// </summary>
public class ModelConfigurationService : IModelConfigurationService
{
    private readonly ILogger<ModelConfigurationService> _logger;
    private readonly ILLamaBackendService _backendService;

    public ModelConfigurationService(
        ILogger<ModelConfigurationService> logger,
        ILLamaBackendService backendService)
    {
        _logger = logger;
        _backendService = backendService;
    }

    /// <inheritdoc/>
    public Task<ModelDetectionResult> DetectModelInfoAsync(string modelPath)
    {
        var result = new ModelDetectionResult
        {
            ModelPath = modelPath,
            IsValid = false
        };

        try
        {
            if (!File.Exists(modelPath))
            {
                result.ErrorMessage = "Model file not found";
                return Task.FromResult(result);
            }

            var fileInfo = new FileInfo(modelPath);
            result.FileSize = fileInfo.Length;

            // Detect format based on file extension
            var extension = Path.GetExtension(modelPath).ToLowerInvariant();
            result.Format = extension switch
            {
                ".gguf" => "GGUF",
                ".ggml" => "GGML",
                _ => "Unknown"
            };

            if (result.Format == "Unknown")
            {
                result.ErrorMessage = "Unsupported model format";
                return Task.FromResult(result);
            }

            // Try to extract model information from filename
            var fileName = Path.GetFileNameWithoutExtension(modelPath);
            result.ModelFamily = DetectModelFamilyFromFilename(fileName);
            result.QuantizationType = DetectQuantizationFromFilename(fileName);

            // Estimate parameters and context size
            result.EstimatedParameters = EstimateParametersFromFileSize(fileInfo.Length, result.QuantizationType);
            result.EstimatedContextSize = EstimateContextSizeFromParameters(result.EstimatedParameters);

            result.IsValid = true;
            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting model info for {ModelPath}", modelPath);
            result.ErrorMessage = ex.Message;
            return Task.FromResult(result);
        }
    }

    /// <inheritdoc/>
    public async Task<ModelConfig> CreateOptimalConfigAsync(string modelPath, Dictionary<string, object>? customSettings = null)
    {
        var detectionResult = await DetectModelInfoAsync(modelPath);

        var config = new ModelConfig
        {
            Architecture = detectionResult.ModelFamily ?? "llama",
            ContextLength = detectionResult.EstimatedContextSize,
            ModelFamily = detectionResult.ModelFamily,
            TotalSize = detectionResult.FileSize
        };

        // Set defaults based on model family
        SetModelFamilyDefaults(config);

        // Apply performance optimizations
        ApplyPerformanceOptimizations(config, detectionResult);

        // Apply custom settings if provided
        if (customSettings != null)
        {
            ApplyCustomSettings(config, customSettings);
        }

        // Auto-detect chat template
        config = AutoDetectChatTemplate(config);

        _logger.LogInformation("Created optimal configuration for {ModelPath}: Family={Family}, Context={Context}, GPU Layers={GpuLayers}",
            modelPath, config.ModelFamily, config.ContextLength, config.PreferredGpuLayers);

        return config;
    }

    /// <inheritdoc/>
    public ModelConfigValidationResult ValidateConfiguration(ModelConfig config)
    {
        var result = new ModelConfigValidationResult { IsValid = true };

        // Validate required fields
        if (string.IsNullOrEmpty(config.Architecture))
        {
            result.Errors.Add("Architecture is required");
            result.IsValid = false;
        }

        if (config.ContextLength == 0)
        {
            result.Errors.Add("Context length must be greater than 0");
            result.IsValid = false;
        }

        // Validate reasonable bounds
        if (config.ContextLength > 1_000_000)
        {
            result.Warnings.Add($"Context length {config.ContextLength} is very large and may cause memory issues");
        }

        if (config.PreferredGpuLayers.HasValue && config.PreferredGpuLayers < 0)
        {
            result.Errors.Add("GPU layers cannot be negative");
            result.IsValid = false;
        }

        if (config.PreferredThreads.HasValue && config.PreferredThreads <= 0)
        {
            result.Errors.Add("Thread count must be greater than 0");
            result.IsValid = false;
        }

        // Validate token settings
        if (string.IsNullOrEmpty(config.BosToken))
        {
            result.Warnings.Add("BOS token is not set, using default '<s>'");
        }

        if (string.IsNullOrEmpty(config.EosToken))
        {
            result.Warnings.Add("EOS token is not set, using default '</s>'");
        }

        return result;
    }

    /// <inheritdoc/>
    public List<PerformanceSuggestion> SuggestOptimizations(ModelConfig config, SystemInfo? systemInfo = null)
    {
        var suggestions = new List<PerformanceSuggestion>();
        var sysInfo = systemInfo ?? GetSystemInfo();

        // GPU optimization suggestions
        if (sysInfo.HasGpu && !config.PreferredGpuLayers.HasValue)
        {
            suggestions.Add(new PerformanceSuggestion
            {
                Type = OptimizationType.GpuUsage,
                Priority = SuggestionPriority.High,
                Message = "Consider enabling GPU acceleration by setting preferred_gpu_layers",
                RecommendedValue = EstimateOptimalGpuLayers(config, sysInfo).ToString()
            });
        }

        // Memory optimization suggestions
        if (config.ContextLength > 8192 && !config.FlashAttention.HasValue)
        {
            suggestions.Add(new PerformanceSuggestion
            {
                Type = OptimizationType.Memory,
                Priority = SuggestionPriority.Medium,
                Message = "Enable flash attention for large context sizes to reduce memory usage",
                RecommendedValue = "true"
            });
        }

        // Thread optimization suggestions
        if (!config.PreferredThreads.HasValue)
        {
            var optimalThreads = Math.Min(sysInfo.CpuCores, 8); // Don't exceed 8 threads typically
            suggestions.Add(new PerformanceSuggestion
            {
                Type = OptimizationType.Threading,
                Priority = SuggestionPriority.Medium,
                Message = $"Set thread count to {optimalThreads} for optimal CPU utilization",
                RecommendedValue = optimalThreads.ToString()
            });
        }

        // Batch size optimization
        if (!config.PreferredBatchSize.HasValue)
        {
            var optimalBatch = EstimateOptimalBatchSize(config);
            suggestions.Add(new PerformanceSuggestion
            {
                Type = OptimizationType.Batching,
                Priority = SuggestionPriority.Low,
                Message = $"Set batch size to {optimalBatch} for balanced performance",
                RecommendedValue = optimalBatch.ToString()
            });
        }

        return suggestions;
    }

    /// <inheritdoc/>
    public async Task<bool> SaveConfigurationAsync(ModelConfig config, string filePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            };

            var json = JsonSerializer.Serialize(config, options);
            await File.WriteAllTextAsync(filePath, json);

            _logger.LogInformation("Configuration saved successfully to {FilePath}", filePath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving configuration to {FilePath}", filePath);
            return false;
        }
    }

    /// <inheritdoc/>
    public ModelConfig AutoDetectChatTemplate(ModelConfig config)
    {
        if (!string.IsNullOrEmpty(config.ChatTemplate))
        {
            return config; // Already has a template
        }

        var family = config.DetectModelFamily().ToLowerInvariant();
        config.TemplateType = family;

        // Set basic template information
        config.SupportsTools = family switch
        {
            "llama" => true,
            "mistral" => true,
            "qwen" => true,
            _ => false
        };

        config.SupportsSystemMessage = family switch
        {
            "llama" => true,
            "mistral" => true,
            "qwen" => true,
            "phi" => false, // Phi models typically don't support system messages well
            _ => true
        };

        return config;
    }

    #region Private Helper Methods

    private string DetectModelFamilyFromFilename(string fileName)
    {
        var lowerName = fileName.ToLowerInvariant();

        if (lowerName.Contains("llama") || lowerName.Contains("alpaca"))
            return "llama";
        if (lowerName.Contains("mistral") || lowerName.Contains("mixtral"))
            return "mistral";
        if (lowerName.Contains("codellama"))
            return "codellama";
        if (lowerName.Contains("phi"))
            return "phi";
        if (lowerName.Contains("gemma"))
            return "gemma";
        if (lowerName.Contains("qwen"))
            return "qwen";

        return "llama"; // Default fallback
    }

    private string? DetectQuantizationFromFilename(string fileName)
    {
        var lowerName = fileName.ToLowerInvariant();

        if (lowerName.Contains("q4_0")) return "Q4_0";
        if (lowerName.Contains("q4_1")) return "Q4_1";
        if (lowerName.Contains("q5_0")) return "Q5_0";
        if (lowerName.Contains("q5_1")) return "Q5_1";
        if (lowerName.Contains("q8_0")) return "Q8_0";
        if (lowerName.Contains("f16")) return "F16";
        if (lowerName.Contains("f32")) return "F32";

        return null;
    }

    private long EstimateParametersFromFileSize(long fileSize, string? quantization)
    {
        // Rough estimation based on typical file sizes
        return quantization switch
        {
            "Q4_0" or "Q4_1" => fileSize * 2, // ~4 bits per parameter
            "Q5_0" or "Q5_1" => fileSize * 8 / 5, // ~5 bits per parameter  
            "Q8_0" => fileSize, // ~8 bits per parameter
            "F16" => fileSize / 2, // 16 bits per parameter
            "F32" => fileSize / 4, // 32 bits per parameter
            _ => fileSize // Conservative estimate
        };
    }

    private uint EstimateContextSizeFromParameters(long parameters)
    {
        // Estimate context size based on parameter count
        return parameters switch
        {
            < 1_000_000_000 => 2048,      // < 1B parameters
            < 7_000_000_000 => 4096,      // 1B-7B parameters
            < 15_000_000_000 => 8192,     // 7B-15B parameters
            < 70_000_000_000 => 4096,     // 15B-70B parameters (memory constrained)
            _ => 2048                      // Very large models (memory constrained)
        };
    }

    private void SetModelFamilyDefaults(ModelConfig config)
    {
        var family = config.ModelFamily?.ToLowerInvariant() ?? "llama";

        (config.BosToken, config.EosToken) = family switch
        {
            "llama" => ("<s>", "</s>"),
            "mistral" => ("<s>", "</s>"),
            "codellama" => ("<s>", "</s>"),
            "phi" => ("", "<|endoftext|>"),
            "gemma" => ("<bos>", "<eos>"),
            "qwen" => ("<|im_start|>", "<|im_end|>"),
            _ => ("<s>", "</s>")
        };
    }

    private void ApplyPerformanceOptimizations(ModelConfig config, ModelDetectionResult detectionResult)
    {
        var systemInfo = GetSystemInfo();

        // Set GPU layers if GPU is available
        if (systemInfo.HasGpu)
        {
            config.PreferredGpuLayers = EstimateOptimalGpuLayers(config, systemInfo);
        }

        // Set thread count
        config.PreferredThreads = Math.Min(systemInfo.CpuCores, 8);

        // Set batch size
        config.PreferredBatchSize = EstimateOptimalBatchSize(config);

        // Memory optimization
        config.UseMmap = true;
        config.UseMlock = systemInfo.AvailableMemoryGB < 8; // Use mlock on low memory systems
    }

    private void ApplyCustomSettings(ModelConfig config, Dictionary<string, object> customSettings)
    {
        foreach (var (key, value) in customSettings)
        {
            try
            {
                switch (key.ToLowerInvariant())
                {
                    case "context_length":
                        config.ContextLength = Convert.ToUInt32(value);
                        break;
                    case "gpu_layers":
                        config.PreferredGpuLayers = Convert.ToInt32(value);
                        break;
                    case "threads":
                        config.PreferredThreads = Convert.ToInt32(value);
                        break;
                    case "batch_size":
                        config.PreferredBatchSize = Convert.ToUInt32(value);
                        break;
                    case "flash_attention":
                        config.FlashAttention = Convert.ToBoolean(value);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error applying custom setting {Key}={Value}", key, value);
            }
        }
    }

    private int EstimateOptimalGpuLayers(ModelConfig config, SystemInfo systemInfo)
    {
        // Conservative GPU layer estimation
        if (config.TotalSize < 2_000_000_000) // < 2GB
            return 35;
        if (config.TotalSize < 4_000_000_000) // < 4GB
            return 28;
        if (config.TotalSize < 8_000_000_000) // < 8GB
            return 20;

        return 15; // Conservative for large models
    }

    private uint EstimateOptimalBatchSize(ModelConfig config)
    {
        return config.ContextLength switch
        {
            <= 2048 => 512,
            <= 4096 => 256,
            <= 8192 => 128,
            _ => 64
        };
    }

    private SystemInfo GetSystemInfo()
    {
        return new SystemInfo
        {
            CpuCores = Environment.ProcessorCount,
            AvailableMemoryGB = GetAvailableMemoryGB(),
            HasGpu = DetectGpuPresence()
        };
    }

    private static int GetAvailableMemoryGB()
    {
        try
        {
            // Get total physical memory in bytes and convert to GB
            var memoryInfo = GC.GetGCMemoryInfo();
            var totalMemoryBytes = memoryInfo.TotalAvailableMemoryBytes;
            return (int)(totalMemoryBytes / (1024L * 1024 * 1024));
        }
        catch
        {
            // Fallback to reasonable default if detection fails
            return 16;
        }
    }

    private static bool DetectGpuPresence()
    {
        try
        {
            // Check for CUDA environment variable as a simple GPU detection
            var cudaPath = Environment.GetEnvironmentVariable("CUDA_PATH");
            var cudaHome = Environment.GetEnvironmentVariable("CUDA_HOME");
            return !string.IsNullOrEmpty(cudaPath) || !string.IsNullOrEmpty(cudaHome);
        }
        catch
        {
            // Fallback to false if detection fails
            return false;
        }
    }

    #endregion
}

#region Supporting Types

/// <summary>
/// Result of model detection
/// </summary>
public class ModelDetectionResult
{
    public string ModelPath { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }
    public long FileSize { get; set; }
    public string Format { get; set; } = string.Empty;
    public string? ModelFamily { get; set; }
    public string? QuantizationType { get; set; }
    public long EstimatedParameters { get; set; }
    public uint EstimatedContextSize { get; set; }
}

/// <summary>
/// Result of configuration validation
/// </summary>
public class ModelConfigValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// Performance optimization suggestion
/// </summary>
public class PerformanceSuggestion
{
    public OptimizationType Type { get; set; }
    public SuggestionPriority Priority { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? RecommendedValue { get; set; }
}

/// <summary>
/// System information for optimization
/// </summary>
public class SystemInfo
{
    public int CpuCores { get; set; }
    public int AvailableMemoryGB { get; set; }
    public bool HasGpu { get; set; }
}

/// <summary>
/// Types of optimizations
/// </summary>
public enum OptimizationType
{
    GpuUsage,
    Memory,
    Threading,
    Batching,
    Context
}

/// <summary>
/// Priority levels for suggestions
/// </summary>
public enum SuggestionPriority
{
    Low,
    Medium,
    High,
    Critical
}

#endregion
