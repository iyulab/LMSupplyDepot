using LLama.Common;
using LMSupplyDepots.External.LLamaEngine.Models;
using LMSupplyDepots.External.LLamaEngine.Logging;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace LMSupplyDepots.External.LLamaEngine.Services;

public interface ILLamaBackendService
{
    bool IsCudaAvailable { get; }
    bool IsVulkanAvailable { get; }
    ModelParams GetOptimalModelParams(string modelPath, ModelConfig? config = null);
}

public class LLamaBackendService(ILogger<LLamaBackendService> logger) : ILLamaBackendService
{
    private readonly ILogger<LLamaBackendService> _logger = new SafeLogger<LLamaBackendService>(logger);
    private readonly ConcurrentDictionary<string, ModelParams> _modelParams = new();
    private int? _gpuLayers;
    private bool _initialized;
    private readonly object _initLock = new();

    // Default context size constants
    private const uint DEFAULT_CONTEXT_SIZE = 2048;
    private const uint MIN_CONTEXT_SIZE = 1024;
    private const uint MAX_CONTEXT_SIZE = 8192;

    public bool IsCudaAvailable { get; private set; }
    public bool IsVulkanAvailable { get; private set; }

    public ModelParams GetOptimalModelParams(string modelPath, ModelConfig? config = null)
    {
        EnsureInitialized();

        return _modelParams.GetOrAdd(modelPath, path =>
        {
            var gpuLayers = _gpuLayers ?? DetectGpuLayers();
            _gpuLayers = gpuLayers;

            var threads = Environment.ProcessorCount;
            uint contextSize = DetermineOptimalContextSize(config);

            var modelParams = new ModelParams(path)
            {
                ContextSize = contextSize,
                BatchSize = DetermineOptimalBatchSize(contextSize),
                Threads = threads,
                GpuLayerCount = gpuLayers,
                MainGpu = 0
            };

            _logger.LogInformation(
                "Model parameters configured - ContextSize: {ContextSize}, BatchSize: {BatchSize}, Threads: {Threads}, GpuLayers: {GpuLayers}",
                modelParams.ContextSize,
                modelParams.BatchSize,
                modelParams.Threads,
                modelParams.GpuLayerCount);

            return modelParams;
        });
    }

    private uint DetermineOptimalContextSize(ModelConfig? config)
    {
        try
        {
            // Check system memory
            var memInfo = GC.GetGCMemoryInfo();
            var totalMemoryGB = memInfo.TotalAvailableMemoryBytes / (1024.0 * 1024.0 * 1024.0);

            // Model's configured context size (or default if not specified)
            uint requestedSize = config?.ContextLength ?? DEFAULT_CONTEXT_SIZE;

            // Maximum allowed context size based on available memory
            uint memoryBasedMaxSize = DetermineMemoryBasedContextSize(totalMemoryGB);

            // Determine actual context size to use
            uint contextSize = Math.Min(requestedSize, memoryBasedMaxSize);
            contextSize = Math.Max(contextSize, MIN_CONTEXT_SIZE);
            contextSize = Math.Min(contextSize, MAX_CONTEXT_SIZE);

            if (contextSize != requestedSize)
            {
                _logger.LogWarning(
                    "Requested context size {RequestedSize} adjusted to {ActualSize} based on available memory ({MemoryGB:F1}GB)",
                    requestedSize, contextSize, totalMemoryGB);
            }

            return contextSize;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error determining optimal context size, using default");
            return Math.Min(config?.ContextLength ?? DEFAULT_CONTEXT_SIZE, MAX_CONTEXT_SIZE);
        }
    }

    private static uint DetermineMemoryBasedContextSize(double totalMemoryGB)
    {
        // Determine maximum context size based on memory size
        if (totalMemoryGB >= 32) return 8192;
        if (totalMemoryGB >= 16) return 4096;
        if (totalMemoryGB >= 8) return 2048;
        return 1024;
    }

    private static uint DetermineOptimalBatchSize(uint contextSize)
    {
        // Adjust batch size based on context size
        if (contextSize > 4096) return 1024;
        if (contextSize > 2048) return 512;
        return 256;
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;

        lock (_initLock)
        {
            if (_initialized) return;
            DetectAvailableBackends();
            _initialized = true;
        }
    }

    private void DetectAvailableBackends()
    {
        try
        {
            IsCudaAvailable = DetectCuda();
            IsVulkanAvailable = DetectVulkan();

            var backendInfo = new List<string>();
            if (IsCudaAvailable) backendInfo.Add("CUDA");
            if (IsVulkanAvailable) backendInfo.Add("Vulkan");

            var backendMessage = backendInfo.Count > 0 ? string.Join(", ", backendInfo) : "None";
            _logger.LogInformation("Detected hardware acceleration: {Backends}", backendMessage);

            // Try to initialize backends in order of preference
            if (IsCudaAvailable && TryInitializeCuda())
            {
                _logger.LogInformation("Successfully initialized CUDA backend");
            }
            else if (IsVulkanAvailable && TryInitializeVulkan())
            {
                _logger.LogInformation("Successfully initialized Vulkan backend");
            }
            else
            {
                _logger.LogWarning("No hardware acceleration available, falling back to CPU");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting available backends");
            IsCudaAvailable = false;
            IsVulkanAvailable = false;
        }
    }

    private int DetectGpuLayers()
    {
        if (IsCudaAvailable)
        {
            try
            {
                if (TryInitializeCuda())
                {
                    var memInfo = GetGpuMemoryInfo();
                    return CalculateOptimalGpuLayers(memInfo);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initialize CUDA backend");
            }
        }
        else if (IsVulkanAvailable)
        {
            try
            {
                if (TryInitializeVulkan())
                {
                    return 20; // Conservative default for Vulkan
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initialize Vulkan backend");
            }
        }

        return 0;
    }

    private bool TryInitializeCuda()
    {
        try
        {
            return TryLoadBackend("LLamaSharp.Backend.Cuda12") ||
                   TryLoadBackend("LLamaSharp.Backend.Cuda11");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing CUDA backend");
            return false;
        }
    }

    private bool TryInitializeVulkan()
    {
        try
        {
            return TryLoadBackend("LLamaSharp.Backend.Vulkan");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing Vulkan backend");
            return false;
        }
    }

    private static bool DetectCuda()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return File.Exists(Path.Combine(Environment.SystemDirectory, "nvcuda.dll"));
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return File.Exists("/usr/lib/x86_64-linux-gnu/libcuda.so") ||
                       File.Exists("/usr/lib/libcuda.so");
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool DetectVulkan()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return File.Exists(Path.Combine(Environment.SystemDirectory, "vulkan-1.dll"));
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return File.Exists("/usr/lib/x86_64-linux-gnu/libvulkan.so") ||
                       File.Exists("/usr/lib/libvulkan.so");
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryLoadBackend(string assemblyName)
    {
        try
        {
            System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyName(
                new System.Reflection.AssemblyName(assemblyName));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private long GetGpuMemoryInfo()
    {
        // In a real implementation, we would get GPU memory info through CUDA/Vulkan API
        // Here we return a default value
        return 4L * 1024 * 1024 * 1024; // 4GB
    }

    private static int CalculateOptimalGpuLayers(long gpuMemoryBytes)
    {
        // Calculate optimal number of layers based on model size and GPU memory
        var gpuMemoryGB = gpuMemoryBytes / (1024.0 * 1024.0 * 1024.0);

        if (gpuMemoryGB >= 8)
            return 32;
        else if (gpuMemoryGB >= 6)
            return 24;
        else if (gpuMemoryGB >= 4)
            return 20;
        else if (gpuMemoryGB >= 2)
            return 16;
        else
            return 8;
    }
}